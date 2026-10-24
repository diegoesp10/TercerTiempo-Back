using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class PlayerStore(AppDbContext db) : IPlayerStore
{
    public Task<Player?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Player?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Players.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Player>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.Players.Where(p => list.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public void Add(Player player) => db.Players.Add(player);
}
