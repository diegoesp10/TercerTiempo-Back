using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Players.LeaveSquad;

/// <summary>El jugador deja de estar vinculado a su ficha (la ficha y sus actas siguen en la plantilla).</summary>
public sealed record LeaveSquadAsPlayerCommand : ICommand<PlayerDto>;

internal sealed class LeaveSquadAsPlayerHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    IUnitOfWork unitOfWork) : ICommandHandler<LeaveSquadAsPlayerCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(LeaveSquadAsPlayerCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken)
            ?? throw new ConflictException(ErrorCode.PlayerNotInSquad);

        squad.UnlinkPlayer(player.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlayerMapper.ToDto(player, null, null, null, null);
    }
}
