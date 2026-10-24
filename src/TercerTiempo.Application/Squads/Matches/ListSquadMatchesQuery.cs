using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Squads.Matches;

public sealed record ListSquadMatchesQuery(Guid SquadId) : IQuery<IReadOnlyList<SquadMatchDto>>;

internal sealed class ListSquadMatchesHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<ListSquadMatchesQuery, IReadOnlyList<SquadMatchDto>>
{
    public async Task<IReadOnlyList<SquadMatchDto>> Handle(ListSquadMatchesQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);

        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var list = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);

        var teamIds = list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var recorded = (await performances.GetBySquadAsync(squad.Id, cancellationToken))
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.Count());

        return list
            .OrderBy(m => m.Matchday)
            .ThenBy(m => m.KickoffAt)
            .Select(m => new SquadMatchDto(MatchMapper.ToDto(m, teamMap), recorded.GetValueOrDefault(m.Id)))
            .ToList();
    }
}
