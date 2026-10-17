using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Performances.ListMyPerformances;

/// <summary>Actas en las que aparece el jugador autenticado (en su plantilla actual), con su partido.</summary>
public sealed record ListMyPerformancesQuery : IQuery<IReadOnlyList<PerformanceWithMatchDto>>;

internal sealed class ListMyPerformancesHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances) : IQueryHandler<ListMyPerformancesQuery, IReadOnlyList<PerformanceWithMatchDto>>
{
    public async Task<IReadOnlyList<PerformanceWithMatchDto>> Handle(ListMyPerformancesQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad?.PlayerEntryOf(player.Id) is not { } entry)
            return [];

        var mine = await performances.GetBySquadPlayerAsync(entry.Id, cancellationToken);
        if (mine.Count == 0) return [];

        var matchMap = (await matches.GetBySeasonAsync(squad.SeasonId, cancellationToken)).ToDictionary(m => m.Id);
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        return mine
            .Where(p => matchMap.ContainsKey(p.MatchId))
            .Select(p => new PerformanceWithMatchDto(
                PerformanceMapper.ToDto(p, entry),
                MatchMapper.ToDto(matchMap[p.MatchId], teamMap)))
            .OrderByDescending(x => x.Match.KickoffAt)
            .ToList();
    }
}
