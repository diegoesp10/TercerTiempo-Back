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

    public void Add(Standing standing) => db.Standings.Add(standing);
}
