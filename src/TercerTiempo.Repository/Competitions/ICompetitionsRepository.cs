using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Competitions.Matchdays;

namespace TercerTiempo.Repository.Competitions;

/// <summary>Ligas (categoría + grupo) sincronizadas desde el proveedor de datos y sus jornadas.</summary>
public interface ICompetitionsRepository
{
    Task<IReadOnlyList<CompetitionDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Partidos de una jornada; con <paramref name="matchday"/> null, la jornada en curso o la próxima.</summary>
    Task<CompetitionMatchdayDto> GetMatchdayAsync(Guid competitionId, int? matchday, CancellationToken cancellationToken = default);
}
