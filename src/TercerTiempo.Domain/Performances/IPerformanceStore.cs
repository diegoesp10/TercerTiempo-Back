namespace TercerTiempo.Domain.Performances;

public interface IPerformanceStore
{
    Task<PlayerMatchPerformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadPlayerAsync(Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAndMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    /// <summary>Líneas con minutos pero sin nota calculada (por ejemplo, las registradas antes de que la nota fuera automática).</summary>
    Task<IReadOnlyList<PlayerMatchPerformance>> GetWithoutRatingAsync(CancellationToken cancellationToken = default);
    void Add(PlayerMatchPerformance performance);
    void Remove(PlayerMatchPerformance performance);
}
