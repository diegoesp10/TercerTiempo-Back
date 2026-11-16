using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Teams.Roster;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Clubs.Roster;

public sealed record GetClubRosterEntryQuery(Guid TeamId, Guid EntryId) : IQuery<RosterEntryDetailDto>;

public sealed record RosterEntryDetailDto(
    RosterEntryDto Entry,
    [property: Description("Partidos en los que aparece en el acta oficial, de la jornada más reciente a la primera.")]
    IReadOnlyList<RosterOfficialAppearanceDto> Official,
    [property: Description("Actas de Tercer Tiempo registradas por el cuerpo técnico del equipo.")]
    IReadOnlyList<RosterInternalAppearanceDto> Internal,
    [property: Description("Resumen de las actas de Tercer Tiempo; null si el cuerpo técnico no ha registrado ninguna.")]
    RosterInternalStatsDto? InternalStats,
    bool HasSquad);

public sealed record RosterOfficialAppearanceDto(MatchDto Match, bool Home, OfficialLineDto Line);

public sealed record RosterInternalAppearanceDto(MatchDto Match, bool Home, PerformanceDto Performance);

public sealed record RosterInternalStatsDto(
    int Appearances,
    int Started,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    int YellowCards,
    int RedCards,
    double? AverageRating);

internal sealed class GetClubRosterEntryHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    RosterStores roster,
    IPerformanceStore performances) : IQueryHandler<GetClubRosterEntryQuery, RosterEntryDetailDto>
{
    public async Task<RosterEntryDetailDto> Handle(GetClubRosterEntryQuery query, CancellationToken cancellationToken)
    {
        var (seasonId, _) = await ClubRoster.ContextAsync(query.TeamId, currentUser, stores, cancellationToken, write: false);
        var entry = await ClubRoster.EntryAsync(query.TeamId, seasonId, query.EntryId, stores.Teams, cancellationToken);
        var details = await RosterEntryDetails.BuildAsync(query.TeamId, seasonId, entry, roster, stores.Squads, performances, cancellationToken);

        return new RosterEntryDetailDto(details.Entry, details.Official, details.Internal, RosterEntryDetails.InternalStats(details.Internal), details.Player is not null);
    }
}

// La ficha de un jugador con todo lo que hay de él en la temporada: actas oficiales (públicas) y actas de Tercer
// Tiempo de su cuerpo técnico (internas). La comparten la ficha del club y la ficha pública.
internal static class RosterEntryDetails
{
    public sealed record Details(
        RosterEntryDto Entry,
        IReadOnlyList<RosterOfficialAppearanceDto> Official,
        IReadOnlyList<RosterInternalAppearanceDto> Internal,
        Squad? Squad,
        SquadPlayer? Player);

    public static async Task<Details> BuildAsync(
        Guid teamId, Guid seasonId, RosterEntry entry, RosterStores roster, ISquadStore squads, IPerformanceStore performances, CancellationToken cancellationToken)
    {
        // La ficha que se pide puede ser una de las varias que la web publica de la misma persona: se enseña la persona.
        // Una ficha retirada no está en la plantilla y va sola, sin actas.
        var allEntries = await roster.Teams.GetRosterAsync(teamId, seasonId, cancellationToken);
        var people = await RosterSummary.PeopleAsync(teamId, seasonId, allEntries, roster.Matches, roster.Sheets, cancellationToken);
        var mine = people.FirstOrDefault(p => p.Person.Entries.Any(e => e.Id == entry.Id))
            ?? new RosterPersonSeason(new RosterPerson(entry, [entry]), []);

        var matches = await roster.Matches.GetTeamMatchesAsync(seasonId, teamId, cancellationToken);
        var teamIds = matches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teams = (await roster.Teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var official = mine.Appearances
            .OrderByDescending(a => a.Match.Matchday)
            .Select(a => new RosterOfficialAppearanceDto(MatchMapper.ToDto(a.Match, teams), a.Home, OfficialSheetMapper.ToDto(a.Line)))
            .ToList();

        var (squad, player) = await SquadPlayerAsync(teamId, seasonId, mine.Person, RosterSummary.Candidates(people), squads, cancellationToken);
        var internalRows = new List<RosterInternalAppearanceDto>();
        if (player is not null)
        {
            var byMatch = matches.ToDictionary(m => m.Id);
            internalRows = (await performances.GetBySquadPlayerAsync(player.Id, cancellationToken))
                .Where(p => byMatch.ContainsKey(p.MatchId))
                .Select(p => (Match: byMatch[p.MatchId], Performance: p))
                .OrderByDescending(x => x.Match.Matchday)
                .Select(x => new RosterInternalAppearanceDto(
                    MatchMapper.ToDto(x.Match, teams), x.Match.HomeTeamId == teamId, PerformanceMapper.ToDto(x.Performance, player)))
                .ToList();
        }

        return new Details(
            RosterEntryDto.From(mine.Person, RosterSummary.Stats(mine.Appearances), RosterSummary.ShirtNumbers(mine.Appearances)),
            official, internalRows, squad, player);
    }

    // La persona enlaza con el jugador de la plantilla interna por la ficha de la que se importó (la principal antes
    // que las otras, y uno en activo antes que uno de baja) o, si no, por el mismo cruce de nombres que se usa con las
    // actas oficiales; de dorsal, el que más ha llevado en ellas frente al habitual que le pone el cuerpo técnico.
    private static async Task<(Squad? Squad, SquadPlayer? Player)> SquadPlayerAsync(
        Guid teamId, Guid seasonId, RosterPerson person, IReadOnlyList<SheetMatcher.Candidate> people, ISquadStore squads, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByTeamAndSeasonAsync(teamId, seasonId, cancellationToken);
        if (squad is null) return (null, null);

        var imported = person.Entries.SelectMany(e => squad.Players.Where(p => p.RosterEntryId == e.Id)).ToList();
        var player = imported.FirstOrDefault(p => p.IsActive) ?? imported.FirstOrDefault();
        if (player is null)
            SheetMatcher.MatchPlayers(people, squad.ActivePlayers).TryGetValue(person.Main.Id, out player);
        return (squad, player);
    }

    public static RosterInternalStatsDto? InternalStats(IReadOnlyList<RosterInternalAppearanceDto> rows)
    {
        var played = rows.Select(r => r.Performance).Where(p => p.MinutesPlayed > 0).ToList();
        if (played.Count == 0) return null;

        var ratings = played.Where(p => p.Rating is not null).Select(p => p.Rating!.Value).ToList();
        return new RosterInternalStatsDto(
            played.Count,
            played.Count(p => p.Started),
            played.Sum(p => p.MinutesPlayed),
            played.Sum(p => p.Goals),
            played.Sum(p => p.Assists),
            played.Sum(p => p.Shots),
            played.Sum(p => p.ShotsOnTarget),
            played.Sum(p => p.KeyPasses),
            played.Sum(p => p.Tackles),
            played.Sum(p => p.Interceptions),
            played.Sum(p => p.Saves),
            played.Sum(p => p.YellowCards),
            played.Count(p => p.RedCard),
            ratings.Count == 0 ? null : Math.Round(ratings.Average(), 1));
    }
}
