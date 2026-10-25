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
        var allEntries = await roster.Teams.GetRosterAsync(teamId, seasonId, cancellationToken);
        var appearances = await RosterSummary.AppearancesAsync(teamId, seasonId, allEntries, roster.Matches, roster.Sheets, cancellationToken);
        var mine = appearances.GetValueOrDefault(entry.Id) ?? [];

        var matches = await roster.Matches.GetTeamMatchesAsync(seasonId, teamId, cancellationToken);
        var teamIds = matches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teams = (await roster.Teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var official = mine
            .OrderByDescending(a => a.Match.Matchday)
            .Select(a => new RosterOfficialAppearanceDto(MatchMapper.ToDto(a.Match, teams), a.Home, OfficialSheetMapper.ToDto(a.Line)))
            .ToList();

        var (squad, player) = await SquadPlayerAsync(teamId, seasonId, entry, allEntries, squads, cancellationToken);
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

        return new Details(RosterEntryDto.From(entry, RosterSummary.Stats(mine)), official, internalRows, squad, player);
    }

    // La ficha enlaza con el jugador de la plantilla interna por id (si se importó de aquí) o, si no, por el mismo
    // cruce de nombre y dorsal que se usa con las actas oficiales.
    private static async Task<(Squad? Squad, SquadPlayer? Player)> SquadPlayerAsync(
        Guid teamId, Guid seasonId, RosterEntry entry, IReadOnlyList<RosterEntry> allEntries, ISquadStore squads, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByTeamAndSeasonAsync(teamId, seasonId, cancellationToken);
        if (squad is null) return (null, null);

        var player = squad.Players.FirstOrDefault(p => p.RosterEntryId == entry.Id);
        if (player is null)
        {
            var candidates = allEntries.Select(r => new SheetMatcher.Candidate(r.Id, r.NormalizedName, r.ShirtNumber));
            var byName = SheetMatcher.MatchPlayers(candidates, squad.ActivePlayers);
            byName.TryGetValue(entry.Id, out player);
        }
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
