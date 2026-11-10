using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class PerformanceStore(AppDbContext db) : IPerformanceStore
{
    public Task<PlayerMatchPerformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Performances.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadPlayerAsync(Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.SquadPlayerId == squadPlayerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.SquadId == squadId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAndMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.SquadId == squadId && p.MatchId == matchId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.MatchId == matchId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.SeasonId == seasonId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMatchPerformance>> GetWithoutRatingAsync(CancellationToken cancellationToken = default) =>
        await db.Performances
            .Where(p => p.Rating == null && p.MinutesPlayed > 0)
            .ToListAsync(cancellationToken);

    public void Add(PlayerMatchPerformance performance) => db.Performances.Add(performance);

    public void Remove(PlayerMatchPerformance performance) => db.Performances.Remove(performance);
}
