using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Players.LeaveTeam;

public sealed record LeaveTeamCommand : ICommand<PlayerDto>;

internal sealed class LeaveTeamHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<LeaveTeamCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(LeaveTeamCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        player.LeaveSquad(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlayerMapper.ToDto(player, null, null, null, null);
    }
}
