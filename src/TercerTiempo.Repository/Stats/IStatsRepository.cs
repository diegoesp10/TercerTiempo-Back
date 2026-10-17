using TercerTiempo.Application.Stats;

namespace TercerTiempo.Repository.Stats;

/// <summary>Panel del jugador y clasificación de la liga.</summary>
public interface IStatsRepository
{
    Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<LeagueTableDto> GetLeagueTableAsync(CancellationToken cancellationToken = default);
}
