using System.Globalization;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

/// <summary>
/// Datos de un partido leídos de su página pública (<c>/evento/...</c>). Es la única fuente para los partidos que
/// WordPress mantiene como "programados" (estado <c>future</c>): la web los muestra, pero su API REST no los devuelve.
/// </summary>
internal sealed record EventPage(
    IReadOnlyList<string> Leagues,
    string? Season,
    int? Matchday,
    DateOnly? Date,
    TimeOnly? Time,
    string? HomeTeamSlug,
    string? AwayTeamSlug,
    int? HomeGoals,
    int? AwayGoals,
    bool Live,
    string? Venue)
{
    /// <summary>Hora que se asume cuando la página no la indica.</summary>
    public static readonly TimeOnly DefaultKickoffTime = new(12, 0);

    public bool IsMatch =>
        Matchday is > 0 && Date is not null && !string.IsNullOrEmpty(HomeTeamSlug) && !string.IsNullOrEmpty(AwayTeamSlug);

    /// <summary>Con marcador y sin la marca "en directo": partido terminado.</summary>
    public bool Played => !Live && HomeGoals is not null && AwayGoals is not null;

    /// <summary>Fecha y hora locales (zona horaria del sitio) del saque inicial.</summary>
    public DateTime? LocalKickoff => Date?.ToDateTime(Time ?? DefaultKickoffTime);

    /// <summary>¿Pertenece a la liga y temporada indicadas? (nombres comparados sin tildes, mayúsculas ni separadores).</summary>
    public bool BelongsTo(string leagueName, string seasonLabel)
    {
        var league = Key(leagueName);
        var inLeague = Leagues.Any(l => Key(l) == league);
        var inSeason = Season is null || Key(Season) == Key(seasonLabel);
        return inLeague && inSeason;
    }

    /// <summary>Clave de comparación: sin entidades HTML, tildes ni mayúsculas, y con guiones largos como guion simple.</summary>
    private static string Key(string value) =>
        NameNormalizer.Normalize(WebUtility.HtmlDecode(value).Replace('–', '-').Replace('—', '-'));
}

internal static class EventPageParser
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy"];
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];

    // Cabeceras de la tabla "Detalles" de SportsPress (la web está en español; se aceptan también las originales en inglés).
    private static readonly string[] DateHeaders = ["fecha", "date"];
    private static readonly string[] TimeHeaders = ["hora", "time"];
    private static readonly string[] LeagueHeaders = ["liga", "league", "competicion"];
    private static readonly string[] SeasonHeaders = ["temporada", "season"];
    private static readonly string[] MatchdayHeaders = ["jornada", "match day", "matchday", "day"];

    public static EventPage Parse(string html)
    {
        using var document = new HtmlParser().ParseDocument(html);

        var details = ReadDetails(document);
        var teams = document.QuerySelectorAll(".sp-event-logos .sp-team-logo");
        var home = teams.Length > 0 ? teams[0] : null;
        var away = teams.Length > 1 ? teams[1] : null;

        return new EventPage(
            Leagues: SplitList(details.Get(LeagueHeaders)),
            Season: details.Get(SeasonHeaders),
            Matchday: ParseInt(details.Get(MatchdayHeaders)),
            Date: ParseDate(details.Get(DateHeaders)),
            Time: ParseTime(details.Get(TimeHeaders)),
            HomeTeamSlug: TeamSlug(home),
            AwayTeamSlug: TeamSlug(away),
            HomeGoals: ParseInt(home?.QuerySelector(".sp-team-result")?.TextContent),
            AwayGoals: ParseInt(away?.QuerySelector(".sp-team-result")?.TextContent),
            // Durante el partido la web pinta el marcador en directo con su propia marca.
            Live: document.QuerySelector(".fa-en-directo-badge, .sp-team-result--directo") is not null,
            Venue: ReadVenue(document));
    }

    private static Details ReadDetails(IDocument document)
    {
        var table = document.QuerySelector("table.sp-event-details");
        if (table is null) return Details.Empty;

        var headers = table.QuerySelectorAll("thead th").Select(th => NameNormalizer.Normalize(th.TextContent)).ToList();
        var cells = table.QuerySelector("tbody tr")?.QuerySelectorAll("td").Select(td => Clean(td.TextContent)).ToList() ?? [];

        return new Details(headers, cells);
    }

    private static string? TeamSlug(IElement? teamLogo)
    {
        var href = teamLogo?.QuerySelector("a[href]")?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out var uri))
            return null;

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : href.Split('?', '#')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? Uri.UnescapeDataString(segments[^1]) : null;
    }

    private static string? ReadVenue(IDocument document)
    {
        // La plantilla propia del sitio pinta "Estadio: <a>Nombre</a>"; la estándar de SportsPress, una tabla de sede.
        var custom = document.QuerySelector(".fa-partido-estadio a") ?? document.QuerySelector(".fa-partido-estadio");
        var name = custom?.TextContent;

        if (custom is not null && custom.LocalName != "a")
            name = name?.Replace("Estadio:", string.Empty, StringComparison.OrdinalIgnoreCase);

        name ??= document.QuerySelector(".sp-template-event-venue .sp-event-venue thead th")?.TextContent;

        var clean = Clean(name);
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int? ParseInt(string? value) =>
        int.TryParse(Clean(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(Clean(value), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParseExact(Clean(value), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    private static string Clean(string? value) => WebUtility.HtmlDecode(value ?? string.Empty).Replace(' ', ' ').Trim();

    private sealed record Details(IReadOnlyList<string> Headers, IReadOnlyList<string> Cells)
    {
        public static readonly Details Empty = new([], []);

        public string? Get(string[] names)
        {
            for (var i = 0; i < Headers.Count && i < Cells.Count; i++)
            {
                if (names.Contains(Headers[i]) && Cells[i].Length > 0)
                    return Cells[i];
            }

            return null;
        }
    }
}
