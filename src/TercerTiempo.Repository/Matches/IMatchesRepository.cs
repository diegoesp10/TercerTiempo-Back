using TercerTiempo.Application.Matches;

namespace TercerTiempo.Repository.Matches;

/// <summary>Detalle de partidos con las actas visibles para el usuario.</summary>
public interface IMatchesRepository
{
    Task<MatchDetailDto> GetAsync(Guid matchId, CancellationToken cancellationToken = default);
}
