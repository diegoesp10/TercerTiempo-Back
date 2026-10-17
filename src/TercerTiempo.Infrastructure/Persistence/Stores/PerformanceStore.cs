using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class PerformanceStore(AppDbContext db) : IPerformanceStore
{
    public Task<PlayerMatchPerformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Performances.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PlayerMatchPerformance?> GetByPlayerAndMatchAsync(Guid playerId, Guid matchId, CancellationToken cancellationToken = default) =>
        db.Performances.FirstOrDefaultAsync(p => p.PlayerId == playerId && p.MatchId == matchId, cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetByPlayerAsync(Guid playerId, Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.PlayerId == playerId && p.SeasonId == seasonId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.MatchId == matchId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.SeasonId == seasonId)
            .ToListAsync(cancellationToken);

    public void Add(PlayerMatchPerformance performance) => db.Performances.Add(performance);

    public void Remove(PlayerMatchPerformance performance) => db.Performances.Remove(performance);
}
