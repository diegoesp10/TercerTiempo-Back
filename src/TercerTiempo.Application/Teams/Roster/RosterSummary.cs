using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.Roster;

public sealed record RosterEntryStatsDto(
    int Appearances,
    int Started,
    int MinutesPlayed,
    int Goals,
    int YellowCards,
    int RedCards);

public sealed record RosterAppearance(Match Match, bool Home, OfficialSheetLine Line);

// Cruza las fichas de la plantilla con las líneas de las actas oficiales del equipo (misma heurística de nombres y
// dorsales que el comparador de actas) para sacar minutos, goles y tarjetas de cada jugador en la temporada.
internal static class RosterSummary
{
    public static async Task<IReadOnlyDictionary<Guid, List<RosterAppearance>>> AppearancesAsync(
        Guid teamId,
        Guid seasonId,
        IReadOnlyList<RosterEntry> roster,
        IMatchStore matches,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken)
    {
        var result = roster.ToDictionary(r => r.Id, _ => new List<RosterAppearance>());
        if (roster.Count == 0) return result;

        var teamMatches = await matches.GetTeamMatchesAsync(seasonId, teamId, cancellationToken);
        if (teamMatches.Count == 0) return result;

        var byMatch = (await sheets.GetByMatchIdsAsync(teamMatches.Select(m => m.Id), cancellationToken))
            .Where(s => s.IsAvailable)
            .ToDictionary(s => s.MatchId);

        var candidates = roster.Select(r => new SheetMatcher.Candidate(r.Id, r.NormalizedName, r.ShirtNumber)).ToList();

        foreach (var match in teamMatches.OrderBy(m => m.Matchday).ThenBy(m => m.KickoffAt))
        {
            if (!byMatch.TryGetValue(match.Id, out var sheet)) continue;

            var home = match.HomeTeamId == teamId;
            var lines = sheet.LinesOf(home ? MatchSide.Home : MatchSide.Away);

            foreach (var (entryId, line) in SheetMatcher.Match(candidates, lines))
                result[entryId].Add(new RosterAppearance(match, home, line));
        }

        return result;
    }

    public static RosterEntryStatsDto? Stats(IReadOnlyList<RosterAppearance> appearances)
    {
        if (appearances.Count == 0) return null;

        var played = appearances.Where(a => a.Line.Played).ToList();
        return new RosterEntryStatsDto(
            played.Count,
            played.Count(a => a.Line.Started),
            played.Sum(a => a.Line.MinutesPlayed),
            played.Sum(a => a.Line.Goals + a.Line.PenaltyGoals),
            played.Sum(a => a.Line.YellowCards),
            played.Count(a => a.Line.RedCard));
    }

    public static async Task<IReadOnlyList<RosterEntryDto>> ListAsync(
        Guid teamId,
        Guid seasonId,
        ITeamStore teams,
        IMatchStore matches,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken)
    {
        var roster = await teams.GetRosterAsync(teamId, seasonId, cancellationToken);
        var appearances = await AppearancesAsync(teamId, seasonId, roster, matches, sheets, cancellationToken);

        return roster
            .OrderBy(r => r.ShirtNumber ?? int.MaxValue)
            .ThenBy(r => r.FullName)
            .Select(r => RosterEntryDto.From(r, Stats(appearances[r.Id])))
            .ToList();
    }
}
