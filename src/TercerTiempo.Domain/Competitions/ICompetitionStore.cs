using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

public interface ICompetitionStore
{
    Task<Competition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Competition?> GetByExternalRefAsync(ExternalRef externalRef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Competition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Competición a la que pertenece una temporada.</summary>
    Task<Competition?> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    /// <summary>Competición en cuya temporada activa participa el equipo (según clasificación o calendario).</summary>
    Task<Competition?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    void Add(Competition competition);
}
