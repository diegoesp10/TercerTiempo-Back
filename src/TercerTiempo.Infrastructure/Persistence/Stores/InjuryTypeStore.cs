using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class InjuryTypeStore(AppDbContext db) : IInjuryTypeStore
{
    public async Task<IReadOnlyList<InjuryTypeDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.InjuryTypes.ToListAsync(cancellationToken);

    public Task<InjuryTypeDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.InjuryTypes.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default) =>
        await db.PlayerInjuries.IgnoreAutoIncludes()
            .GroupBy(i => i.Type)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Code, g => g.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public void Add(InjuryTypeDefinition type) => db.InjuryTypes.Add(type);

    public void Remove(InjuryTypeDefinition type) => db.InjuryTypes.Remove(type);
}
