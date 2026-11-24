using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class TerritoryStore(AppDbContext db) : ITerritoryStore
{
    public async Task<IReadOnlyList<Territory>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Territories.OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<Territory?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        db.Territories.Local.FirstOrDefault(t => t.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? await db.Territories.FirstOrDefaultAsync(t => t.Name == name.Trim(), cancellationToken);

    public void Add(Territory territory) => db.Territories.Add(territory);
}
