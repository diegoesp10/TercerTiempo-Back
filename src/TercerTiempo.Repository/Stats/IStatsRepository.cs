using TercerTiempo.Application.Stats;

namespace TercerTiempo.Repository.Stats;

/// <summary>Clasificación de una liga.</summary>
public interface IStatsRepository
{
    /// <summary>Clasificación de la competición indicada o, si es null, de la plantilla del usuario autenticado.</summary>
    Task<LeagueTableDto> GetLeagueTableAsync(Guid? competitionId, CancellationToken cancellationToken = default);
}
