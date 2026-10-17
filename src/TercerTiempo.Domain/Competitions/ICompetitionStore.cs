using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

public interface ICompetitionStore
{
    Task<Competition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Competition?> GetByExternalRefAsync(ExternalRef externalRef, CancellationToken cancellationToken = default);

    /// <summary>Competición que tiene una temporada marcada como actual (la que se sincroniza y se muestra).</summary>
    Task<Competition?> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<Competition?> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    void Add(Competition competition);
}
