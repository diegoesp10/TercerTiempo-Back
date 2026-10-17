using TercerTiempo.Application.Stats;

namespace TercerTiempo.Repository.Stats;

/// <summary>Panel del jugador y clasificación de una liga.</summary>
public interface IStatsRepository
{
    Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>Clasificación de la competición indicada o, si es null, de la del equipo del jugador autenticado.</summary>
    Task<LeagueTableDto> GetLeagueTableAsync(Guid? competitionId, CancellationToken cancellationToken = default);
}
