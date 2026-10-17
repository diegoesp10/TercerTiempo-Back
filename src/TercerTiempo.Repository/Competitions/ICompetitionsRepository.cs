using TercerTiempo.Application.Competitions;

namespace TercerTiempo.Repository.Competitions;

/// <summary>Ligas (categoría + grupo) sincronizadas desde el proveedor de datos.</summary>
public interface ICompetitionsRepository
{
    Task<IReadOnlyList<CompetitionDto>> ListAsync(CancellationToken cancellationToken = default);
}
