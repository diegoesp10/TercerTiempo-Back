using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Coaches;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class CoachStore(AppDbContext db) : ICoachStore
{
    public Task<Coach?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Coaches.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Coach?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Coaches.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Coach>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        return await db.Coaches.Where(c => list.Contains(c.Id)).ToListAsync(cancellationToken);
    }

    public void Add(Coach coach) => db.Coaches.Add(coach);
}
