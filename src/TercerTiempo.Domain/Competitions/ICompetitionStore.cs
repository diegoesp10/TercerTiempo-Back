using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

public interface ICompetitionStore
{
    Task<Competition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Competition?> GetByExternalRefAsync(ExternalRef externalRef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Competition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Competition?> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // La liga del equipo en esa temporada de la app (sin ella, en la temporada en curso de cada liga).
    Task<Competition?> GetByTeamAsync(Guid teamId, Guid? seasonYearId = null, CancellationToken cancellationToken = default);

    void Add(Competition competition);
}
