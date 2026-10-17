using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.GetCurrentPlayer;
using TercerTiempo.Application.Players.JoinTeam;
using TercerTiempo.Application.Players.LeaveTeam;

namespace TercerTiempo.Repository.Players;

internal sealed class PlayersRepository(
    IQueryHandler<GetCurrentPlayerQuery, PlayerDto> getCurrent,
    ICommandHandler<JoinTeamCommand, PlayerDto> joinTeam,
    ICommandHandler<LeaveTeamCommand, PlayerDto> leaveTeam) : IPlayersRepository
{
    public Task<PlayerDto> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        getCurrent.Handle(new GetCurrentPlayerQuery(), cancellationToken);

    public Task<PlayerDto> JoinTeamAsync(JoinTeamCommand command, CancellationToken cancellationToken = default) =>
        joinTeam.Handle(command, cancellationToken);

    public Task<PlayerDto> LeaveTeamAsync(CancellationToken cancellationToken = default) =>
        leaveTeam.Handle(new LeaveTeamCommand(), cancellationToken);
}
