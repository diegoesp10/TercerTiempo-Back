using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Performances.ListMyPerformances;

/// <summary>Todos los rendimientos del jugador autenticado en la temporada activa, con su partido.</summary>
public sealed record ListMyPerformancesQuery : IQuery<IReadOnlyList<PerformanceWithMatchDto>>;

internal sealed class ListMyPerformancesHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ICompetitionStore competitions,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances) : IQueryHandler<ListMyPerformancesQuery, IReadOnlyList<PerformanceWithMatchDto>>
{
    public async Task<IReadOnlyList<PerformanceWithMatchDto>> Handle(ListMyPerformancesQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var (_, season) = await Resolvers.CurrentSeasonAsync(competitions, cancellationToken);

        var mine = await performances.GetByPlayerAsync(player.Id, season.Id, cancellationToken);
        if (mine.Count == 0) return [];

        var matchMap = (await matches.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(m => m.Id);
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
