using TercerTiempo.Application.Performances;

namespace TercerTiempo.Repository.Performances;

/// <summary>Historial de rendimiento del jugador autenticado.</summary>
public interface IPerformancesRepository
{
    Task<IReadOnlyList<PerformanceWithMatchDto>> GetMineAsync(CancellationToken cancellationToken = default);
}
