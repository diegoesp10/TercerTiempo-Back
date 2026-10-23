using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

internal sealed record CatalogEntry(string Code, string Name);

internal static partial class AsturfutbolPages
{
    private static readonly HtmlParser Parser = new();

    // ------------------------------------------------------------------ catálogos

    public static IReadOnlyList<CatalogEntry> ParseSelect(string html, string selectName)
    {
        var document = Parser.ParseDocument(html);
        var select = document.QuerySelector($"select[name='{selectName}']");
        if (select is null) return [];

        return select.QuerySelectorAll("option")
            .Select(o => new CatalogEntry(o.GetAttribute("value")?.Trim() ?? string.Empty, Clean(o.TextContent)))
            .Where(e => e.Code.Length > 0 && e.Code != "0" && e.Name.Length > 0)
            .ToList();
    }

    [GeneratedRegex("""new Array\((?<items>(?:"[^"]*"\s*,?\s*)+)\)""")]
    private static partial Regex JsArrayPattern();

    [GeneratedRegex("\"(?<item>[^\"]*)\"")]
    private static partial Regex JsStringPattern();

    public static IReadOnlyList<CatalogEntry> ParseJsArray(string script)
    {
        var match = JsArrayPattern().Match(script);
        if (!match.Success) return [];

        var items = JsStringPattern().Matches(match.Groups["items"].Value).Select(m => m.Groups["item"].Value).ToList();
        var result = new List<CatalogEntry>();

        for (var i = 0; i + 1 < items.Count; i += 2)
        {
            var code = items[i].Trim();
            var name = Clean(items[i + 1]);
            if (code.Length > 0 && code != "0" && name.Length > 0)
                result.Add(new CatalogEntry(code, name));
        }

        return result;
    }

    // ------------------------------------------------------------------ jornada

    [GeneratedRegex(@"CodActa=(?<code>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SheetCodePattern();

    [GeneratedRegex(@"Codigo_Equipo=(?<code>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TeamCodePattern();

    [GeneratedRegex(@"(?<d>\d{1,2})-(?<m>\d{1,2})-(?<y>\d{4})")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b(?<h>\d{1,2}):(?<mi>\d{2})\b")]
    private static partial Regex TimePattern();

    public static IReadOnlyList<OfficialFixture> ParseMatchday(string html)
    {
        var document = Parser.ParseDocument(html);
        var result = new List<OfficialFixture>();

        foreach (var table in document.QuerySelectorAll("#divResultados_ table, .table-borderless table"))
        {
            var teamLinks = table.QuerySelectorAll("a[href*='Codigo_Equipo=']")
                .Where(a => a.Closest("table") == table)
                .ToList();
            if (teamLinks.Count != 2) continue;

            var sheetLink = table.QuerySelectorAll("a[href*='CodActa=']").FirstOrDefault(a => a.Closest("table") == table);
            var sheetCode = sheetLink is null ? null : SheetCodePattern().Match(sheetLink.GetAttribute("href") ?? string.Empty).Groups["code"].Value;

            var schedule = table.QuerySelectorAll(".horario").Select(s => Clean(s.TextContent)).ToList();
            DateOnly? date = null;
            TimeOnly? time = null;

            foreach (var text in schedule)
            {
                if (date is null && DatePattern().Match(text) is { Success: true } d)
                    date = new DateOnly(int.Parse(d.Groups["y"].Value), int.Parse(d.Groups["m"].Value), int.Parse(d.Groups["d"].Value));
                if (time is null && TimePattern().Match(text) is { Success: true } t)
                    time = new TimeOnly(int.Parse(t.Groups["h"].Value), int.Parse(t.Groups["mi"].Value));
            }

            result.Add(new OfficialFixture(
                Clean(teamLinks[0].TextContent), TeamCode(teamLinks[0]),
                Clean(teamLinks[1].TextContent), TeamCode(teamLinks[1]),
                date, time,
                string.IsNullOrEmpty(sheetCode) ? null : sheetCode));
        }

        return result;
    }

    private static string? TeamCode(IElement link)
    {
        var code = TeamCodePattern().Match(link.GetAttribute("href") ?? string.Empty).Groups["code"].Value;
        return code.Length > 0 ? code : null;
    }

    // ------------------------------------------------------------------ acta

    [GeneratedRegex(@"\((?<min>\d{1,3})'(?:\s*\+\s*(?<extra>\d{1,2}))?\)")]
    private static partial Regex MinutePattern();

    public static OfficialSheetData? ParseSheet(string html, int matchLength)
    {
        var document = Parser.ParseDocument(html);

        var teamNames = document.QuerySelectorAll(".font_widgetL, .font_widgetV").Select(e => Clean(e.TextContent)).Where(t => t.Length > 0).ToList();
        if (teamNames.Count < 2) return null;

        var home = teamNames[0];
        var away = teamNames[1];

        var referee = document.QuerySelectorAll("h5")
            .Select(h => h.TextContent)
            .Where(t => Clean(t).StartsWith("Árbitro", StringComparison.OrdinalIgnoreCase) && !Clean(t).StartsWith("Árbitro Asistente", StringComparison.OrdinalIgnoreCase))
            .Select(t => Clean(Regex.Replace(t, "^\\s*Árbitro\\s*", string.Empty)))
            .FirstOrDefault(t => t.Length > 0);

        // Un bloque .dashboard-stat por equipo (su título es el nombre del equipo) con titulares, suplentes,
        // sustituciones y tarjetas; otro bloque "Goles" común.
        var blocks = document.QuerySelectorAll(".dashboard-stat").ToList();
        var homeBlock = blocks.FirstOrDefault(b => Title(b) == home);
        var awayBlock = blocks.LastOrDefault(b => Title(b) == away);
        var goalsBlock = blocks.FirstOrDefault(b => Title(b).Equals("Goles", StringComparison.OrdinalIgnoreCase));

        var lines = new List<SheetLineBuilder>();
        var events = new List<OfficialEventData>();

        if (homeBlock is not null) ReadTeam(homeBlock, MatchSide.Home, lines, events, matchLength);
        if (awayBlock is not null && !ReferenceEquals(awayBlock, homeBlock)) ReadTeam(awayBlock, MatchSide.Away, lines, events, matchLength);

        if (goalsBlock is not null) ReadGoals(goalsBlock, lines, events);

        var ordered = events.OrderBy(e => e.Minute).ThenBy(e => e.ExtraMinute ?? 0).ToList();

        return new OfficialSheetData(
            referee, home, away,
            ScoreOf(lines, MatchSide.Home), ScoreOf(lines, MatchSide.Away),
            lines.Select(l => l.Build()).ToList(),
            ordered);
    }

    private static int? ScoreOf(List<SheetLineBuilder> lines, MatchSide side)
    {
        if (lines.Count == 0) return null;
        return lines.Where(l => l.Side == side).Sum(l => l.Goals + l.PenaltyGoals) + lines.Where(l => l.Side != side).Sum(l => l.OwnGoals);
    }

    private static string Title(IElement block) => Clean(block.QuerySelector(".number")?.TextContent ?? string.Empty);

    private static void ReadTeam(IElement block, MatchSide side, List<SheetLineBuilder> lines, List<OfficialEventData> events, int matchLength)
    {
        // Titulares y suplentes: tabla que sigue a cada cabecera <h5><strong>Titulares</strong></h5>.
        foreach (var header in block.QuerySelectorAll("h5, h4"))
        {
            var title = Clean(header.TextContent);
            var table = NextTable(header);
            if (table is null) continue;

            if (title.Equals("Titulares", StringComparison.OrdinalIgnoreCase) || title.Equals("Suplentes", StringComparison.OrdinalIgnoreCase))
            {
                var started = title.Equals("Titulares", StringComparison.OrdinalIgnoreCase);
                foreach (var row in table.QuerySelectorAll("tr"))
                {
                    var cells = row.QuerySelectorAll("td").Select(c => Clean(c.TextContent)).Where(t => t.Length > 0).ToList();
                    if (cells.Count == 0) continue;

                    var (shirt, name) = SplitShirt(cells);
                    if (name.Length == 0) continue;

                    lines.Add(new SheetLineBuilder(side, shirt, name, started, matchLength));
                }
            }
            else if (title.Equals("Sustituciones", StringComparison.OrdinalIgnoreCase))
            {
                // Cada sustitución es una tabla: fila 1 entra (flecha verde), fila 2 sale con el minuto.
                foreach (var subTable in TablesAfter(header))
                {
                    var rows = subTable.QuerySelectorAll("tr").ToList();
                    if (rows.Count < 2) continue;

                    var inRow = rows.FirstOrDefault(r => r.QuerySelector(".fa-arrow-left") is not null) ?? rows[0];
                    var outRow = rows.FirstOrDefault(r => r.QuerySelector(".fa-arrow-right") is not null) ?? rows[1];

                    var (inShirt, inName) = SplitShirt(Cells(inRow));
                    var (outShirt, outText) = SplitShirt(Cells(outRow));
                    var (minute, extra, outName) = SplitMinute(outText);
                    if (minute is null) (minute, extra, inName) = SplitMinute(inName);
                    if (minute is null) continue;

                    Find(lines, side, inShirt, inName)?.EnterAt(minute.Value);
                    Find(lines, side, outShirt, outName)?.LeaveAt(minute.Value);

                    events.Add(new OfficialEventData(side, OfficialEventType.Substitution, minute.Value, extra, inName, outName));
                }
            }
            else if (title.Equals("Tarjetas", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var row in table.QuerySelectorAll("tr"))
                {
                    var image = row.QuerySelector("img")?.GetAttribute("src") ?? string.Empty;
                    var text = Clean(string.Join(' ', row.QuerySelectorAll("td").Select(c => c.TextContent)));
                    var (minute, extra, name) = SplitMinute(text);
                    if (minute is null || name.Length == 0) continue;

                    var type = image.Contains("roja", StringComparison.OrdinalIgnoreCase) ? OfficialEventType.RedCard
                        : image.Contains("dobl", StringComparison.OrdinalIgnoreCase) ? OfficialEventType.SecondYellow
                        : OfficialEventType.YellowCard;

                    var line = Find(lines, side, null, name);
                    if (line is not null)
                    {
                        if (type == OfficialEventType.YellowCard) line.Yellow(minute.Value);
                        else line.Red(minute.Value);
                    }

                    events.Add(new OfficialEventData(side, type, minute.Value, extra, name, null));
                }
            }
        }
    }

    private static void ReadGoals(IElement block, List<SheetLineBuilder> lines, List<OfficialEventData> events)
    {
        foreach (var row in block.QuerySelectorAll("table tr"))
        {
            var cells = row.QuerySelectorAll("td").ToList();
            if (cells.Count < 2) continue;

            var kind = (cells[0].QuerySelector("a[title]")?.GetAttribute("title") ?? string.Empty).ToLowerInvariant();
            var type = kind.Contains("penalti") ? OfficialEventType.PenaltyGoal
                : kind.Contains("propia") ? OfficialEventType.OwnGoal
                : OfficialEventType.Goal;

            var (minute, extra, name) = SplitMinute(Clean(cells[1].TextContent));
            if (minute is null || name.Length == 0) continue;

            var scorer = Find(lines, null, null, name);
            var side = scorer?.Side ?? MatchSide.Home;

            if (scorer is not null)
            {
                if (type == OfficialEventType.Goal) scorer.Goals++;
                else if (type == OfficialEventType.PenaltyGoal) scorer.PenaltyGoals++;
                else scorer.OwnGoals++;
            }

            events.Add(new OfficialEventData(side, type, minute.Value, extra, name, null));
        }
    }

    // ------------------------------------------------------------------ utilidades

    private static IElement? NextTable(IElement header)
    {
        for (var sibling = header.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (sibling.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase)) return sibling;
            if (sibling.TagName is "H4" or "H5") return null;
        }
        return null;
    }

    private static IEnumerable<IElement> TablesAfter(IElement header)
    {
        for (var sibling = header.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (sibling.TagName is "H4" or "H5") yield break;
            if (sibling.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase)) yield return sibling;
        }
    }

    private static List<string> Cells(IElement row) =>
        row.QuerySelectorAll("td").Select(c => Clean(c.TextContent)).Where(t => t.Length > 0).ToList();

    private static (int? Shirt, string Name) SplitShirt(List<string> cells)
    {
        int? shirt = null;
        string name = string.Empty;

        foreach (var cell in cells)
        {
            if (shirt is null && int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                shirt = number;
            else if (name.Length == 0 && cell.Any(char.IsLetter))
                name = cell;
        }

        return (shirt, name);
    }

    private static (int? Minute, int? Extra, string Name) SplitMinute(string text)
    {
        var match = MinutePattern().Match(text);
        if (!match.Success)
            return (null, null, Clean(text));

        var minute = int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture);
        int? extra = match.Groups["extra"].Success ? int.Parse(match.Groups["extra"].Value, CultureInfo.InvariantCulture) : null;
        var name = Clean(text.Remove(match.Index, match.Length));
        return (minute, extra, name);
    }

    private static SheetLineBuilder? Find(List<SheetLineBuilder> lines, MatchSide? side, int? shirt, string name)
    {
        var normalized = NameNormalizer.Normalize(name.Replace(',', ' '));
        var candidates = lines.Where(l => side is null || l.Side == side).ToList();

        return candidates.FirstOrDefault(l => l.NormalizedName == normalized)
            ?? (shirt is { } s ? candidates.FirstOrDefault(l => l.Shirt == s) : null);
    }

    private static string Clean(string text) =>
        Regex.Replace(WebUtility.HtmlDecode(text).Replace(' ', ' '), @"\s+", " ").Trim();
}
