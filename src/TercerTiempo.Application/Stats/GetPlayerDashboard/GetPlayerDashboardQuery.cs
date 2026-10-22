using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Stats.GetPlayerDashboard;

public sealed record GetPlayerDashboardQuery : IQuery<PlayerDashboardDto>;

internal sealed class GetPlayerDashboardHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    ICompetitionStore competitions,
    ITeamStore teams,
    IMatchStore matches,
    IPerformanceStore performances,
    IStandingStore standings) : IQueryHandler<GetPlayerDashboardQuery, PlayerDashboardDto>
{
    private const int RecentCount = 5;

    public async Task<PlayerDashboardDto> Handle(GetPlayerDashboardQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad?.PlayerEntryOf(player.Id) is not { } entry)
            return new PlayerDashboardDto(PlayerMapper.ToDto(player, null, null, null, null), null, IndividualStatsDto.Empty, null, null, []);

        var (competition, season) = await Resolvers.SeasonOfSquadAsync(competitions, squad, cancellationToken);
        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);

        var myPerformances = await performances.GetBySquadPlayerAsync(entry.Id, cancellationToken);
        var seasonMatches = await matches.GetBySeasonAsync(season.Id, cancellationToken);
        var table = await standings.GetBySeasonAsync(season.Id, cancellationToken);

        // Solo se compara contra actas visibles: plantillas públicas de la liga y la propia.
        var comparableSquadIds = (await squads.GetBySeasonAsync(season.Id, cancellationToken))
            .Where(s => s.Id == squad.Id || s.Visibility == SquadVisibility.Public)
            .Select(s => s.Id)
            .ToHashSet();

        var comparable = (await performances.GetBySeasonAsync(season.Id, cancellationToken))
            .Where(p => comparableSquadIds.Contains(p.SquadId))
            .ToList();

        var individual = PlayerStatsCalculator.Individual(myPerformances);
        var teamImpact = PlayerStatsCalculator.TeamImpact(squad.TeamId, seasonMatches, myPerformances);
        var league = PlayerStatsCalculator.LeagueComparison(entry.Id, squad.TeamId, table, comparable);

        var matchMap = seasonMatches.ToDictionary(m => m.Id);
        var teamIds = seasonMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var recent = myPerformances
            .Where(p => matchMap.ContainsKey(p.MatchId))
            .Select(p => new PerformanceWithMatchDto(
                PerformanceMapper.ToDto(p, entry),
                MatchMapper.ToDto(matchMap[p.MatchId], teamMap)))
            .OrderByDescending(x => x.Match.KickoffAt)
            .Take(RecentCount)
            .ToList();

        return new PlayerDashboardDto(
            PlayerMapper.ToDto(player, squad, team, competition, season),
            SeasonDto.From(competition, season),
            individual,
            teamImpact,
            league,
            recent);
    }
}
