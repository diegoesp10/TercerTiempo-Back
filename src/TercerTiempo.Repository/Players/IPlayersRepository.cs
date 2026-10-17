using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Stats;

namespace TercerTiempo.Repository.Players;

/// <summary>Lo que puede hacer un jugador: vincularse a su plantilla y consultar sus estadísticas.</summary>
public interface IPlayersRepository
{
    Task<SquadInviteDto> GetInviteAsync(string inviteCode, CancellationToken cancellationToken = default);
    Task<PlayerDto> JoinSquadAsync(JoinSquadAsPlayerCommand command, CancellationToken cancellationToken = default);
    Task<PlayerDto> LeaveSquadAsync(CancellationToken cancellationToken = default);
    Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceWithMatchDto>> GetMyPerformancesAsync(CancellationToken cancellationToken = default);
}
