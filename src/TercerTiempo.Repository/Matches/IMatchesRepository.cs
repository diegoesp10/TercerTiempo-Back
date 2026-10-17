using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;

namespace TercerTiempo.Repository.Matches;

/// <summary>Detalle de partidos y registro del rendimiento propio en ellos.</summary>
public interface IMatchesRepository
{
    Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<PerformanceDto> LogPerformanceAsync(Guid matchId, LogPerformanceRequest request, CancellationToken cancellationToken = default);
}
