namespace TercerTiempo.Domain.Squads;

public interface IInjuryStore
{
    Task<PlayerInjury?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerInjury>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    // Las de varias fichas a la vez (la misma persona en las plantillas de otras temporadas del club).
    Task<IReadOnlyList<PlayerInjury>> GetBySquadPlayersAsync(IReadOnlyCollection<Guid> squadPlayerIds, CancellationToken cancellationToken = default);
    // Las abiertas que aún no han pasado a otra temporada: todas, o las de unas plantillas.
    Task<IReadOnlyList<PlayerInjury>> GetOpenAsync(IReadOnlyCollection<Guid>? squadIds = null, CancellationToken cancellationToken = default);
    // La de la temporada siguiente que continúa esta; null si no pasó o se borró allí.
    Task<PlayerInjury?> GetContinuationAsync(Guid injuryId, CancellationToken cancellationToken = default);
    void Add(PlayerInjury injury);
    void Remove(PlayerInjury injury);
}
