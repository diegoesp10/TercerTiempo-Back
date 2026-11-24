using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class BodySiteStore(AppDbContext db) : IBodySiteStore
{
    public async Task<IReadOnlyList<BodySite>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.BodySites.ToListAsync(cancellationToken);

    public Task<BodySite?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.BodySites.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default) =>
        await db.PlayerInjuries.IgnoreAutoIncludes()
            .Where(i => i.SiteCode != null)
            .GroupBy(i => i.SiteCode!)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Code, g => g.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public void Add(BodySite site) => db.BodySites.Add(site);

    public void Remove(BodySite site) => db.BodySites.Remove(site);
}
