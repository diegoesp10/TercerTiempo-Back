using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Sanctions;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

internal static partial class AsturfutbolPages
{
    private const string PlayersSection = "jugadores";

    // «Técnicos, Auxiliares y Delegados».
    private const string StaffSectionStart = "tecnicos";

    // La página de sanciones de un grupo (NFG_ConsultaSanciones) trae toda la temporada: por cada reunión del comité, un
    // título con su fecha; dentro, cada jornada que trató con su fecha y, por secciones (jugadores; técnicos, auxiliares
    // y delegados; equipos; resoluciones especiales), una fila por sanción: equipo y nombre, tipo de licencia, artículo y
    // motivo, que acaba con lo que cuesta («(2 partidos)», «(7 días inhabilitación)»). Los títulos van dentro de
    // document.write y las filas fuera, así que se lee en orden con expresiones en vez de con el árbol de la página.
    [GeneratedRegex("""
        Sanciones\s+impuestas\s+por\s+(?<committee>[^<">]+?)\s+en\s+su\s+reuni.n\s+del\s+d.a\s+(?<meeting>\d{1,2}-\d{1,2}-\d{4})
        |Jornada:\s*(?<matchday>\d{1,2})\s*\((?<matchdayOn>\d{1,2}/\d{1,2}/\d{4})\)
        |<td\s+colspan=["']?100[^>]*>\s*<b>\s*(?:<i[^>]*>\s*</i>)?\s*(?<section>[^<]+?)\s*</b>
        |<tr>\s*<td[^>]*min-width:\s*150px[^>]*>(?<who>.*?)</td>\s*<td[^>]*>(?<licence>.*?)</td>\s*<td[^>]*>(?<article>.*?)</td>\s*<td[^>]*>(?<reason>.*?)</td>\s*</tr>
        """, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex SanctionsTokenPattern();

    // Lo que cuesta, al final del motivo: «(1 partido)», «(4 partidos)».
    [GeneratedRegex(@"\(\s*(?<matches>\d{1,2})\s+partidos?\b[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SanctionMatchesPattern();

    // O, al cuerpo técnico, los días: «3 semanas … (21 días inhabilitación)».
    [GeneratedRegex(@"\(\s*(?<days>\d{1,3})\s+d[ií]as?\b[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SanctionDaysPattern();

    // Cómo empieza el motivo: «4 Partidos Agredir a otro», «1 partido por doble amonestación».
    [GeneratedRegex(@"^\s*\d{1,2}\s+(?:partidos?|semanas?|mes(?:es)?|d[ií]as?)\b\s*(?:por\s+)?", RegexOptions.IgnoreCase)]
    private static partial Regex SanctionLeadPattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    public static IReadOnlyList<CommitteeMeetingData> ParseSanctions(string html)
    {
        var blocks = new List<(string Committee, DateOnly MeetingOn, int Matchday, DateOnly? MatchdayOn, List<CommitteeSanctionData> Sanctions)>();
        (string Committee, DateOnly On)? meeting = null;
        var section = string.Empty;
        var current = -1;

        foreach (Match token in SanctionsTokenPattern().Matches(html))
        {
            if (token.Groups["committee"].Success)
            {
                meeting = ParseDate(token.Groups["meeting"].Value, "d-M-yyyy") is { } on ? (CellText(token.Groups["committee"].Value), on) : null;
                current = -1;
            }
            else if (token.Groups["matchday"].Success)
            {
                section = string.Empty;
                current = -1;
                if (meeting is not { } m || !int.TryParse(token.Groups["matchday"].Value, out var matchday) || matchday < 1) continue;

                var matchdayOn = ParseDate(token.Groups["matchdayOn"].Value, "d/M/yyyy");
                current = blocks.FindIndex(b => b.Committee == m.Committee && b.MeetingOn == m.On && b.Matchday == matchday);
                if (current < 0)
                {
                    blocks.Add((m.Committee, m.On, matchday, matchdayOn, []));
                    current = blocks.Count - 1;
                }
            }
            else if (token.Groups["section"].Success)
            {
                section = Domain.Common.NameNormalizer.Normalize(CellText(token.Groups["section"].Value));
            }
            else if (current >= 0 && KindOf(section) is { } kind && ReadSanction(token, kind) is { } sanction)
            {
                blocks[current].Sanctions.Add(sanction);
            }
        }

        return blocks
            .Select(b => new CommitteeMeetingData(
                b.Committee.Length > CommitteeResolution.MaxCommitteeLength ? b.Committee[..CommitteeResolution.MaxCommitteeLength] : b.Committee,
                b.MeetingOn, b.Matchday, b.MatchdayOn, b.Sanctions))
            .ToList();
    }

    private static CommitteeSanctionKind? KindOf(string section) =>
        section == PlayersSection ? CommitteeSanctionKind.Player
        : section.StartsWith(StaffSectionStart, StringComparison.Ordinal) ? CommitteeSanctionKind.Staff
        : null;

    // Solo las que cuestan partidos y, al cuerpo técnico, también las inhabilitaciones por días; las amonestaciones (salen
    // de las actas), no.
    private static CommitteeSanctionData? ReadSanction(Match row, CommitteeSanctionKind kind)
    {
        var who = LineBreakPattern().Split(row.Groups["who"].Value).Select(CellText).Where(p => p.Length > 0).ToList();
        if (who.Count < 2) return null;

        var reason = CellText(row.Groups["reason"].Value);
        var matches = 0;
        int? days = null;
        var cost = SanctionMatchesPattern().Match(reason);
        if (cost.Success && int.TryParse(cost.Groups["matches"].Value, out var count) && count >= 1)
        {
            matches = count;
        }
        else if (kind == CommitteeSanctionKind.Staff
            && SanctionDaysPattern().Match(reason) is { Success: true } byDays
            && int.TryParse(byDays.Groups["days"].Value, out var length) && length >= 1)
        {
            cost = byDays;
            days = length;
        }
        else
        {
            return null;
        }

        var detail = SanctionLeadPattern().Replace(reason[..cost.Index], string.Empty).Trim().TrimEnd(',', '.', ';').Trim();
        if (detail.Length == 0) detail = reason[..cost.Index].Trim();
        if (detail.Length == 0) return null;
        detail = char.ToUpper(detail[0], CultureInfo.GetCultureInfo("es-ES")) + detail[1..];

        return new CommitteeSanctionData(
            Cap(who[0], CommitteeSanction.MaxNameLength),
            Cap(string.Join(' ', who.Skip(1)), CommitteeSanction.MaxNameLength),
            Cap(CellText(row.Groups["article"].Value), CommitteeSanction.MaxArticleLength),
            Cap(detail, CommitteeSanction.MaxReasonLength),
            matches,
            kind,
            days);
    }

    private static string CellText(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(TagPattern().Replace(html, " ")).Replace(' ', ' '), @"\s+", " ").Trim();

    private static DateOnly? ParseDate(string value, string format) =>
        DateOnly.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string Cap(string value, int length) => value.Length > length ? value[..length].TrimEnd() : value;
}
