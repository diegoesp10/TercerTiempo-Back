namespace TercerTiempo.Domain.Performances;

public interface IPerformanceStore
{
    Task<PlayerMatchPerformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlayerMatchPerformance?> GetByPlayerAndMatchAsync(Guid playerId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetByPlayerAsync(Guid playerId, Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    void Add(PlayerMatchPerformance performance);
    void Remove(PlayerMatchPerformance performance);
}
