using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class StandingStore(AppDbContext db) : IStandingStore
{
    public async Task<IReadOnlyList<Standing>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Standings
            .Where(s => s.SeasonId == seasonId)
            .OrderBy(s => s.Position)
            .ToListAsync(cancellationToken);

    public Task<Standing?> GetByTeamAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default) =>
        db.Standings.FirstOrDefaultAsync(s => s.SeasonId == seasonId && s.TeamId == teamId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountBySeasonsAsync(IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default)
    {
        var list = seasonIds.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, int>();
        return await db.Standings
            .Where(s => list.Contains(s.SeasonId))
            .GroupBy(s => s.SeasonId)
            .Select(g => new { SeasonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SeasonId, x => x.Count, cancellationToken);
    }

    public void Add(Standing standing) => db.Standings.Add(standing);
}
