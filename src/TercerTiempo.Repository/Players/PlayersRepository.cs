using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Performances.ListMyPerformances;
using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.GetInvite;
using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Application.Players.LeaveSquad;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Stats;
using TercerTiempo.Application.Stats.GetPlayerDashboard;

namespace TercerTiempo.Repository.Players;

internal sealed class PlayersRepository(
    IQueryHandler<GetSquadInviteQuery, SquadInviteDto> getInvite,
    ICommandHandler<JoinSquadAsPlayerCommand, PlayerDto> joinSquad,
    ICommandHandler<LeaveSquadAsPlayerCommand, PlayerDto> leaveSquad,
    IQueryHandler<GetPlayerDashboardQuery, PlayerDashboardDto> getDashboard,
    IQueryHandler<ListMyPerformancesQuery, IReadOnlyList<PerformanceWithMatchDto>> listPerformances) : IPlayersRepository
{
    public Task<SquadInviteDto> GetInviteAsync(string inviteCode, CancellationToken cancellationToken = default) =>
        getInvite.Handle(new GetSquadInviteQuery(inviteCode), cancellationToken);

    public Task<PlayerDto> JoinSquadAsync(JoinSquadAsPlayerCommand command, CancellationToken cancellationToken = default) =>
        joinSquad.Handle(command, cancellationToken);

    public Task<PlayerDto> LeaveSquadAsync(CancellationToken cancellationToken = default) =>
        leaveSquad.Handle(new LeaveSquadAsPlayerCommand(), cancellationToken);

    public Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        getDashboard.Handle(new GetPlayerDashboardQuery(), cancellationToken);

    public Task<IReadOnlyList<PerformanceWithMatchDto>> GetMyPerformancesAsync(CancellationToken cancellationToken = default) =>
        listPerformances.Handle(new ListMyPerformancesQuery(), cancellationToken);
}
