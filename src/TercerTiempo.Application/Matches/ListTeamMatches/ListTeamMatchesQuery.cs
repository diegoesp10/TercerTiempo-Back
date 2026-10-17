using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Matches.ListTeamMatches;

/// <summary>Calendario y resultados de un equipo en la temporada activa.</summary>
public sealed record ListTeamMatchesQuery(Guid TeamId) : IQuery<IReadOnlyList<MatchDto>>;

internal sealed class ListTeamMatchesHandler(
    ICurrentUser currentUser,
    ICompetitionStore competitions,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances) : IQueryHandler<ListTeamMatchesQuery, IReadOnlyList<MatchDto>>
{
    public async Task<IReadOnlyList<MatchDto>> Handle(ListTeamMatchesQuery query, CancellationToken cancellationToken)
    {
        var (_, season) = await Resolvers.CurrentSeasonAsync(competitions, cancellationToken);

        var list = await matches.GetTeamMatchesAsync(season.Id, query.TeamId, cancellationToken);

        var teamIds = list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var myPerformances = currentUser.PlayerId is { } playerId
            ? (await performances.GetByPlayerAsync(playerId, season.Id, cancellationToken)).ToDictionary(p => p.MatchId, p => p.Id)
            : [];

        return list
            .OrderBy(m => m.Matchday)
            .ThenBy(m => m.KickoffAt)
            .Select(m => MatchMapper.ToDto(m, teamMap, myPerformances.TryGetValue(m.Id, out var id) ? id : null))
            .ToList();
    }
}
