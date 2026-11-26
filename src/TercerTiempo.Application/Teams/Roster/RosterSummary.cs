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

internal sealed record RosterPersonSeason(RosterPerson Person, IReadOnlyList<RosterAppearance> Appearances);

// Cruza las fichas de la plantilla con las líneas de las actas oficiales del equipo (la misma heurística que el
// comparador de actas) para sacar minutos, goles, tarjetas y dorsales de cada jugador en la temporada. La ficha no
// tiene dorsal, así que el cruce va en dos pasadas: primero solo por nombre y después, para lo que el nombre deja en
// el aire (dos «Pelayo» en el mismo acta, un «Hugo» suelto con tres Hugos en la plantilla), con el dorsal que más ha
// llevado cada ficha en lo ya cruzado. La segunda no toca lo que resolvió la primera.
internal static class RosterSummary
{
    public static async Task<IReadOnlyDictionary<Guid, List<RosterAppearance>>> AppearancesAsync(
        Guid teamId,
        Guid seasonId,
        IReadOnlyList<RosterEntry> roster,
        IMatchStore matches,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken) =>
        await AppearancesAsync(teamId, roster, roster.Count == 0 ? [] : await matches.GetTeamMatchesAsync(seasonId, teamId, cancellationToken), sheets, cancellationToken);

    // Con los partidos del equipo en la temporada ya leídos (la ficha del equipo los usa también para el cuerpo técnico).
    public static async Task<IReadOnlyDictionary<Guid, List<RosterAppearance>>> AppearancesAsync(
        Guid teamId,
        IReadOnlyList<RosterEntry> roster,
        IReadOnlyList<Match> teamMatches,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken)
    {
        var result = roster.ToDictionary(r => r.Id, _ => new List<RosterAppearance>());
        if (roster.Count == 0 || teamMatches.Count == 0) return result;

        var byMatch = (await sheets.GetAvailableLinesByMatchIdsAsync(teamMatches.Select(m => m.Id), cancellationToken))
            .ToDictionary(s => s.MatchId);

        var byName = roster.Select(r => new SheetMatcher.Candidate(r.Id, r.NormalizedName, null)).ToList();
        var crossed = teamMatches
            .Where(m => byMatch.ContainsKey(m.Id))
            .OrderBy(m => m.Matchday)
            .ThenBy(m => m.KickoffAt)
            .Select(m =>
            {
                var home = m.HomeTeamId == teamId;
                var lines = byMatch[m.Id].LinesOf(home ? MatchSide.Home : MatchSide.Away).ToList();
                return (Match: m, Home: home, Lines: lines, Found: SheetMatcher.Match(byName, lines).ToDictionary());
            })
            .ToList();

        foreach (var sheet in crossed)
            foreach (var (entryId, line) in sheet.Found)
                result[entryId].Add(new RosterAppearance(sheet.Match, sheet.Home, line));

        var withUsualShirt = Candidates(roster, result);
        foreach (var sheet in crossed)
        {
            var taken = sheet.Found.Values.Select(l => l.Id).ToHashSet();
            var open = SheetMatcher.Match(
                withUsualShirt.Where(c => !sheet.Found.ContainsKey(c.Id)),
                sheet.Lines.Where(l => !taken.Contains(l.Id)));

            foreach (var (entryId, line) in open)
                result[entryId].Add(new RosterAppearance(sheet.Match, sheet.Home, line));
        }

        foreach (var appearances in result.Values)
            appearances.Sort((a, b) => (a.Match.Matchday, a.Match.KickoffAt).CompareTo((b.Match.Matchday, b.Match.KickoffAt)));

        return result;
    }

    // Cada ficha con el dorsal que más ha llevado en las actas ya cruzadas: es la pista de dorsal del cruce.
    private static List<SheetMatcher.Candidate> Candidates(IReadOnlyList<RosterEntry> roster, IReadOnlyDictionary<Guid, List<RosterAppearance>> appearances) =>
        roster
            .Select(r => new SheetMatcher.Candidate(
                r.Id, r.NormalizedName, appearances.TryGetValue(r.Id, out var mine) ? ShirtNumbers(mine).FirstOrDefault()?.Number : null))
            .ToList();

    public static List<SheetMatcher.Candidate> Candidates(IEnumerable<RosterPersonSeason> people) =>
        people
            .Select(p => new SheetMatcher.Candidate(p.Person.Main.Id, p.Person.Main.NormalizedName, ShirtNumbers(p.Appearances).FirstOrDefault()?.Number))
            .ToList();

    // Las personas de la plantilla, cada una con las actas de todas sus fichas (la web publica a algunos jugadores dos
    // veces). Dos fichas que salen en la misma acta son dos jugadores, se llamen como se llamen: no se juntan.
    public static async Task<IReadOnlyList<RosterPersonSeason>> PeopleAsync(
        Guid teamId,
        Guid seasonId,
        IReadOnlyList<RosterEntry> roster,
        IMatchStore matches,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken) =>
        People(roster, await AppearancesAsync(teamId, seasonId, roster, matches, sheets, cancellationToken));

    private static List<RosterPersonSeason> People(IReadOnlyList<RosterEntry> roster, IReadOnlyDictionary<Guid, List<RosterAppearance>> appearances)
    {
        bool InTheSameSheet(RosterEntry a, RosterEntry b) => appearances[a.Id].Any(x => appearances[b.Id].Any(y => y.Match.Id == x.Match.Id));

        return RosterPeople.Group(roster, InTheSameSheet)
            .Select(person => new RosterPersonSeason(
                person,
                person.Entries.SelectMany(e => appearances[e.Id]).OrderBy(a => a.Match.Matchday).ThenBy(a => a.Match.KickoffAt).ToList()))
            .ToList();
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

    // El dorsal de cada jornada es el de su línea del acta, haya jugado o se quedase en el banquillo.
    public static IReadOnlyList<ShirtNumberUsageDto> ShirtNumbers(IReadOnlyList<RosterAppearance> appearances) =>
        ShirtNumberUsage.From(appearances
            .Where(a => a.Line.ShirtNumber is not null)
            .Select(a => (a.Line.ShirtNumber!.Value, a.Match.KickoffAt)));

    public static async Task<IReadOnlyList<RosterEntryDto>> ListAsync(
        Guid teamId,
        Guid seasonId,
        ITeamStore teams,
        IMatchStore matches,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken)
    {
        var roster = await teams.GetRosterAsync(teamId, seasonId, cancellationToken);
        return Entries(await PeopleAsync(teamId, seasonId, roster, matches, sheets, cancellationToken));
    }

    public static async Task<IReadOnlyList<RosterEntryDto>> ListAsync(
        Guid teamId,
        Guid seasonId,
        IReadOnlyList<Match> teamMatches,
        ITeamStore teams,
        IOfficialSheetStore sheets,
        CancellationToken cancellationToken)
    {
        var roster = await teams.GetRosterAsync(teamId, seasonId, cancellationToken);
        return Entries(People(roster, await AppearancesAsync(teamId, roster, teamMatches, sheets, cancellationToken)));
    }

    private static List<RosterEntryDto> Entries(IEnumerable<RosterPersonSeason> people)
    {
        return people
            .OrderBy(p => p.Person.Position is { } position ? (int)position : int.MaxValue)
            .ThenBy(p => p.Person.Main.NormalizedName, StringComparer.Ordinal)
            .Select(p => RosterEntryDto.From(p.Person, Stats(p.Appearances), ShirtNumbers(p.Appearances)))
            .ToList();
    }
}
