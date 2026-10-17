using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Performances.ListMyPerformances;

/// <summary>Todos los rendimientos del jugador autenticado en la temporada de su plantilla actual, con su partido.</summary>
public sealed record ListMyPerformancesQuery : IQuery<IReadOnlyList<PerformanceWithMatchDto>>;

internal sealed class ListMyPerformancesHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances) : IQueryHandler<ListMyPerformancesQuery, IReadOnlyList<PerformanceWithMatchDto>>
{
    public async Task<IReadOnlyList<PerformanceWithMatchDto>> Handle(ListMyPerformancesQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        if (player.CurrentMembership is not { } membership)
            return [];

        var mine = await performances.GetByPlayerAsync(player.Id, membership.SeasonId, cancellationToken);
        if (mine.Count == 0) return [];

        var matchMap = (await matches.GetBySeasonAsync(membership.SeasonId, cancellationToken)).ToDictionary(m => m.Id);
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        return mine
            .Where(p => matchMap.ContainsKey(p.MatchId))
            .Select(p => new PerformanceWithMatchDto(
                PerformanceMapper.ToDto(p, player),
                MatchMapper.ToDto(matchMap[p.MatchId], teamMap, p.Id)))
            .OrderByDescending(x => x.Match.KickoffAt)
            .ToList();
    }
}
