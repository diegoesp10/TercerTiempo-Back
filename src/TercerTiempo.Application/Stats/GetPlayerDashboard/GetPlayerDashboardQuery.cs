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
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Stats.GetPlayerDashboard;

public sealed record GetPlayerDashboardQuery : IQuery<PlayerDashboardDto>;

internal sealed class GetPlayerDashboardHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
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

        var competition = await competitions.GetCurrentAsync(cancellationToken);
        var season = competition?.CurrentSeason;

        if (competition is null || season is null)
            return new PlayerDashboardDto(PlayerMapper.ToDto(player, null, null, null), null, IndividualStatsDto.Empty, null, null, []);

        var membership = player.MembershipFor(season.Id);
        var team = membership is null ? null : await teams.GetByIdAsync(membership.TeamId, cancellationToken);

        var myPerformances = await performances.GetByPlayerAsync(player.Id, season.Id, cancellationToken);
        var seasonMatches = await matches.GetBySeasonAsync(season.Id, cancellationToken);
        var table = await standings.GetBySeasonAsync(season.Id, cancellationToken);
        var seasonPerformances = await performances.GetBySeasonAsync(season.Id, cancellationToken);

        var individual = PlayerStatsCalculator.Individual(myPerformances);

        var teamImpact = membership is null
            ? null
            : PlayerStatsCalculator.TeamImpact(membership.TeamId, seasonMatches, myPerformances);

        var league = PlayerStatsCalculator.LeagueComparison(player.Id, membership?.TeamId, table, seasonPerformances);

        var matchMap = seasonMatches.ToDictionary(m => m.Id);
        var teamIds = seasonMatches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var recent = myPerformances
            .Where(p => matchMap.ContainsKey(p.MatchId))
            .Select(p => new PerformanceWithMatchDto(
                PerformanceMapper.ToDto(p, player),
                MatchMapper.ToDto(matchMap[p.MatchId], teamMap, p.Id)))
            .OrderByDescending(x => x.Match.KickoffAt)
            .Take(RecentCount)
            .ToList();

        return new PlayerDashboardDto(
            PlayerMapper.ToDto(player, membership, team, season),
            SeasonDto.From(competition, season),
            individual,
            teamImpact,
            league,
            recent);
    }
}
