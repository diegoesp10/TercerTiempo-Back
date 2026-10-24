using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Matches.ListTeamMatches;

public sealed record ListTeamMatchesQuery(Guid TeamId) : IQuery<IReadOnlyList<MatchDto>>;

internal sealed class ListTeamMatchesHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    ITeamStore teams) : IQueryHandler<ListTeamMatchesQuery, IReadOnlyList<MatchDto>>
{
    public async Task<IReadOnlyList<MatchDto>> Handle(ListTeamMatchesQuery query, CancellationToken cancellationToken)
    {
        var (_, season) = await Resolvers.SeasonOfTeamAsync(competitions, query.TeamId, cancellationToken);

        var list = await matches.GetTeamMatchesAsync(season.Id, query.TeamId, cancellationToken);

        var teamIds = list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        return list
            .OrderBy(m => m.Matchday)
            .ThenBy(m => m.KickoffAt)
            .Select(m => MatchMapper.ToDto(m, teamMap))
            .ToList();
    }
}
