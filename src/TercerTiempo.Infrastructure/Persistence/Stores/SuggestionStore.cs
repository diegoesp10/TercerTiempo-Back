using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class SuggestionStore(AppDbContext db) : IPerformanceSuggestionStore
{
    public Task<PerformanceSuggestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PerformanceSuggestions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PerformanceSuggestion>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.PerformanceSuggestions.Where(s => s.SquadId == squadId).OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PerformanceSuggestion>> GetByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        await db.PerformanceSuggestions.Where(s => s.PlayerId == playerId).OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);

    public void Add(PerformanceSuggestion suggestion) => db.PerformanceSuggestions.Add(suggestion);
}
