namespace TercerTiempo.Domain.Performances;

public interface IPerformanceStore
{
    Task<PlayerMatchPerformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadPlayerAsync(Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAndMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // Las de la temporada que entran en las medias de la liga: las de esa plantilla y las de las plantillas públicas.
    Task<IReadOnlyList<PlayerMatchPerformance>> GetComparableBySeasonAsync(Guid seasonId, Guid ownSquadId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerMatchPerformance>> GetWithoutRatingAsync(CancellationToken cancellationToken = default);
    void Add(PlayerMatchPerformance performance);
    void Remove(PlayerMatchPerformance performance);
}
