using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.GetSquad;

/// <summary>Detalle de una plantilla: visible para su cuerpo técnico, sus jugadores y, si es pública, para todos.</summary>
public sealed record GetSquadQuery(Guid SquadId) : IQuery<SquadDetailDto>;

internal sealed class GetSquadHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICoachStore coaches,
    IPlayerStore players,
    ITeamStore teams,
    ICompetitionStore competitions) : IQueryHandler<GetSquadQuery, SquadDetailDto>
{
    public async Task<SquadDetailDto> Handle(GetSquadQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException("Plantilla", query.SquadId);

        SquadAccess.EnsureCanView(squad, currentUser);

        return await SquadMapper.ToDetailAsync(squad, currentUser, coaches, players, teams, competitions, cancellationToken);
    }
}
