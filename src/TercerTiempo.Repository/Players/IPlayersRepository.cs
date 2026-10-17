using TercerTiempo.Application.Players;
using TercerTiempo.Application.Players.JoinTeam;

namespace TercerTiempo.Repository.Players;

/// <summary>Perfil del jugador autenticado y su pertenencia a una plantilla.</summary>
public interface IPlayersRepository
{
    Task<PlayerDto> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<PlayerDto> JoinTeamAsync(JoinTeamCommand command, CancellationToken cancellationToken = default);
    Task<PlayerDto> LeaveTeamAsync(CancellationToken cancellationToken = default);
}
