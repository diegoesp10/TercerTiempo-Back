using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Infrastructure.OfficialSheets.BeSoccer;

internal static partial class BeSoccerPages
{
    private static readonly HtmlParser Parser = new();

    [GeneratedRegex(@"/competicion/equipos/[^/""]+/\d{4}/(?<group>grupo\d+)$")]
    private static partial Regex GroupLinkPattern();

    [GeneratedRegex(@"/equipo/(?<slug>[^/?#""]+)$")]
    private static partial Regex TeamLinkPattern();

    [GeneratedRegex(@"/partido/(?<home>[^/]+)/(?<away>[^/]+)/(?<id>\d+)$")]
    private static partial Regex MatchLinkPattern();

    [GeneratedRegex(@"(?<min>\d{1,3})\s*'?\s*(?:\+\s*(?<extra>\d{1,2}))?")]
    private static partial Regex MinutePattern();

    [GeneratedRegex(@"(?<!\S)(?<home>\d+)\s*-\s*(?<away>\d+)(?!\S)")]
    private static partial Regex ScorePattern();

    // ------------------------------------------------------------------ competición

    // Grupos de la competición ("grupo1"...); "" si no tiene grupos; vacío si la página no es de una competición.
    public static IReadOnlyList<string> ParseGroups(string html)
    {
        var document = Parser.ParseDocument(html);
        var groups = document.QuerySelectorAll("a[href]")
            .Select(a => GroupLinkPattern().Match(Path(a.GetAttribute("href"))))
            .Where(m => m.Success)
            .Select(m => m.Groups["group"].Value)
            .Distinct()
            .OrderBy(g => int.Parse(g["grupo".Length..], CultureInfo.InvariantCulture))
            .ToList();

        if (groups.Count > 0) return groups;
        return ParseTeams(html).Count > 0 ? [string.Empty] : [];
    }

    public static IReadOnlyList<DirectoryTeam> ParseTeams(string html)
    {
        var document = Parser.ParseDocument(html);
        return document.QuerySelectorAll("a[data-cy='team'][href]")
            .Select(a => (Match: TeamLinkPattern().Match(Path(a.GetAttribute("href"))), Name: Clean(a.QuerySelector(".name")?.TextContent ?? a.TextContent)))
            .Where(t => t.Match.Success && t.Name.Length > 0)
            .Select(t => new DirectoryTeam(t.Match.Groups["slug"].Value, t.Name))
            .DistinctBy(t => t.Code)
            .ToList();
    }

    // ------------------------------------------------------------------ partidos de un equipo

    public static IReadOnlyList<DirectoryFixture> ParseFixtures(string html)
    {
        var document = Parser.ParseDocument(html);
        var result = new List<DirectoryFixture>();

        foreach (var link in document.QuerySelectorAll("a.match-link[href]"))
        {
            var match = MatchLinkPattern().Match(Path(link.GetAttribute("href")));
            if (!match.Success) continue;

            DateTimeOffset? startsAt = DateTimeOffset.TryParse(link.GetAttribute("starttime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
            var (home, away, _) = ReadEvent(link);
            if (home is null || away is null) continue;

            result.Add(new DirectoryFixture(
                $"{match.Groups["home"].Value}/{match.Groups["away"].Value}/{match.Groups["id"].Value}",
                match.Groups["home"].Value, home,
                match.Groups["away"].Value, away,
                startsAt));
        }

        return result.DistinctBy(f => f.Code).ToList();
    }

    // ------------------------------------------------------------------ acta

    // Alineaciones (dorsal, titular o suplente, árbitro) de una página y eventos (goles, tarjetas y cambios con su
    // minuto) de la otra. Los jugadores se cruzan por el enlace a su ficha, que es único.
    public static OfficialSheetData? ParseSheet(string lineupsHtml, string? eventsHtml, int matchLength)
    {
        var lineups = Parser.ParseDocument(lineupsHtml);
        var (home, away, description) = ReadEvent(lineups);
        if (home is null || away is null) return null;

        var referee = lineups.QuerySelectorAll("a[data-cy='referee']")
            .Where(a => Clean(a.NextElementSibling?.TextContent ?? string.Empty).Equals("Árbitro", StringComparison.OrdinalIgnoreCase))
            .Select(a => Clean(a.TextContent))
            .FirstOrDefault(t => t.Length > 0);

        var lines = new List<SheetLineBuilder>();
        var byLink = new Dictionary<string, SheetLineBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var player in lineups.QuerySelectorAll("a.col-bench[data-cy='starterPlayer'], a.col-bench[data-cy='benchPlayer']"))
        {
            var name = Clean(player.QuerySelector(".name")?.TextContent ?? string.Empty);
            if (name.Length == 0) continue;

            var side = player.ClassList.Contains("visitor") ? MatchSide.Away : MatchSide.Home;
            int? shirt = int.TryParse(Clean(player.QuerySelector(".number")?.TextContent ?? string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
            var line = new SheetLineBuilder(side, shirt, name, player.GetAttribute("data-cy") == "starterPlayer", matchLength);
            lines.Add(line);
            byLink.TryAdd(Path(player.GetAttribute("href")), line);
        }

        var events = new List<OfficialEventData>();
        if (eventsHtml is not null && lines.Count > 0)
            ReadEvents(Parser.ParseDocument(eventsHtml), lines, byLink, events);

        // Tras los dos puntos: "Vallobin CD 0 - 5 Navarro" (sin marcador si aún no se ha jugado).
        var score = ScorePattern().Match((description ?? string.Empty).Split(':')[^1]);
        int? homeGoals = score.Success ? int.Parse(score.Groups["home"].Value, CultureInfo.InvariantCulture) : null;
        int? awayGoals = score.Success ? int.Parse(score.Groups["away"].Value, CultureInfo.InvariantCulture) : null;

        return new OfficialSheetData(
            referee, home, away, homeGoals, awayGoals,
            lines.Select(l => l.Build()).ToList(),
            events.OrderBy(e => e.Minute).ThenBy(e => e.ExtraMinute ?? 0).ToList());
    }

    private static void ReadEvents(IDocument document, List<SheetLineBuilder> lines, Dictionary<string, SheetLineBuilder> byLink, List<OfficialEventData> events)
    {
        foreach (var row in document.QuerySelectorAll(".table-played-match.all-events"))
        {
            var minuteMatch = MinutePattern().Match(Clean(row.QuerySelector(".min")?.TextContent ?? string.Empty));
            if (!minuteMatch.Success) continue;
            var minute = int.Parse(minuteMatch.Groups["min"].Value, CultureInfo.InvariantCulture);
            int? extra = minuteMatch.Groups["extra"].Success ? int.Parse(minuteMatch.Groups["extra"].Value, CultureInfo.InvariantCulture) : null;

            foreach (var (column, side) in new[] { (".col-side.left", MatchSide.Home), (".col-side.right", MatchSide.Away) })
            {
                var cell = row.QuerySelector(column);
                var kind = cell?.QuerySelector(".inner-rows > img[alt]")?.GetAttribute("alt")?.ToLowerInvariant();
                if (cell is null || string.IsNullOrEmpty(kind)) continue;

                if (kind.StartsWith("sustituc", StringComparison.Ordinal))
                {
                    var entering = cell.QuerySelectorAll("li").FirstOrDefault(li => li.QuerySelector(".event-19") is not null)?.QuerySelector("a[href]");
                    var leaving = cell.QuerySelectorAll("li").FirstOrDefault(li => li.QuerySelector(".event-18") is not null)?.QuerySelector("a[href]");
                    if (entering is null || leaving is null) continue;

                    var @in = Find(lines, byLink, side, entering);
                    var @out = Find(lines, byLink, side, leaving);
                    @in?.EnterAt(minute);
                    @out?.LeaveAt(minute);
                    events.Add(new OfficialEventData(side, OfficialEventType.Substitution, minute, extra,
                        @in?.Name ?? Clean(entering.TextContent), @out?.Name ?? Clean(leaving.TextContent)));
                    continue;
                }

                var playerLink = cell.QuerySelector("a[data-cy='eventOrd'][href]");
                if (playerLink is null) continue;
                var type = kind switch
                {
                    _ when kind.Contains("propia") => OfficialEventType.OwnGoal,
                    _ when kind.Contains("penalti") => OfficialEventType.PenaltyGoal,
                    _ when kind.StartsWith("gol", StringComparison.Ordinal) => OfficialEventType.Goal,
                    _ when kind.Contains("2ª amarilla") || kind.Contains("doble") => OfficialEventType.SecondYellow,
                    _ when kind.Contains("amarilla") => OfficialEventType.YellowCard,
                    _ when kind.Contains("roja") => OfficialEventType.RedCard,
                    _ => (OfficialEventType?)null,
                };
                if (type is null) continue;

                var line = Find(lines, byLink, type == OfficialEventType.OwnGoal ? null : side, playerLink);
                switch (type)
                {
                    case OfficialEventType.Goal when line is not null: line.Goals++; break;
                    case OfficialEventType.PenaltyGoal when line is not null: line.PenaltyGoals++; break;
                    case OfficialEventType.OwnGoal when line is not null: line.OwnGoals++; break;
                    case OfficialEventType.YellowCard or OfficialEventType.SecondYellow: line?.Yellow(minute); break;
                    case OfficialEventType.RedCard: line?.Red(minute); break;
                }

                // El gol en propia puerta figura en el lado del equipo beneficiado; el acta lo apunta al del jugador.
                events.Add(new OfficialEventData(line?.Side ?? side, type.Value, minute, extra, line?.Name ?? Clean(playerLink.TextContent), null));
            }
        }
    }

    private static SheetLineBuilder? Find(List<SheetLineBuilder> lines, Dictionary<string, SheetLineBuilder> byLink, MatchSide? side, IElement link)
    {
        if (byLink.TryGetValue(Path(link.GetAttribute("href")), out var line)) return line;

        var name = Clean(link.TextContent);
        return lines.FirstOrDefault(l => (side is null || l.Side == side) && l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ utilidades

    // Equipos y descripción del partido en su JSON-LD (SportsEvent): "Partido de ... Jornada 6: Vallobin CD 0 - 5 Navarro".
    private static (string? Home, string? Away, string? Description) ReadEvent(IParentNode root)
    {
        foreach (var script in root.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var item = json.RootElement;
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("@type", out var type) || type.GetString() != "SportsEvent") continue;

                var home = item.TryGetProperty("homeTeam", out var h) && h.TryGetProperty("name", out var hn) ? Clean(hn.GetString() ?? string.Empty) : null;
                var away = item.TryGetProperty("awayTeam", out var a) && a.TryGetProperty("name", out var an) ? Clean(an.GetString() ?? string.Empty) : null;
                var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(home) && !string.IsNullOrEmpty(away)) return (home, away, description);
            }
            catch (JsonException) { }
        }
        return (null, null, null);
    }

    private static string Path(string? href)
    {
        if (string.IsNullOrEmpty(href)) return string.Empty;
        var path = Uri.TryCreate(href, UriKind.Absolute, out var uri) ? uri.AbsolutePath : href.Split('?', '#')[0];
        return path.TrimEnd('/');
    }

    private static string Clean(string text) =>
        Regex.Replace(WebUtility.HtmlDecode(text).Replace(' ', ' '), @"\s+", " ").Trim();
}
