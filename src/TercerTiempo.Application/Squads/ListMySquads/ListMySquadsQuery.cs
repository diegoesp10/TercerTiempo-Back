using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.ListMySquads;

public sealed record ListMySquadsQuery : IQuery<IReadOnlyList<SquadSummaryDto>>;

internal sealed class ListMySquadsHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    TimeProvider clock) : IQueryHandler<ListMySquadsQuery, IReadOnlyList<SquadSummaryDto>>
{
    public async Task<IReadOnlyList<SquadSummaryDto>> Handle(ListMySquadsQuery query, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, coaches, cancellationToken);

        var result = new List<SquadSummaryDto>();
        foreach (var squad in await squads.GetByCoachAsync(coach.Id, SquadAccess.Today(clock), cancellationToken))
            result.Add(await SquadMapper.ToSummaryAsync(squad, coach.Id, clock, teams, competitions, cancellationToken));

        return result;
    }
}
