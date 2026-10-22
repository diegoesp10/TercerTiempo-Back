using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Squads.GetSquad;

public sealed record GetSquadQuery(Guid SquadId) : IQuery<SquadDetailDto>;

internal sealed class GetSquadHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions,
    TimeProvider clock) : IQueryHandler<GetSquadQuery, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(GetSquadQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);

        SquadAccess.EnsureCanView(squad, currentUser, clock);

        return await SquadMapper.ToDetailAsync(squad, currentUser, clock, coaches, players, teams, competitions, cancellationToken);
    }
}
