using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class InjuryStore(AppDbContext db) : IInjuryStore
{
    public Task<PlayerInjury?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PlayerInjuries.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PlayerInjury>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.PlayerInjuries
            .Where(i => i.SquadId == squadId)
            .OrderByDescending(i => i.StartedOn)
            .ToListAsync(cancellationToken);

    public void Add(PlayerInjury injury) => db.PlayerInjuries.Add(injury);

    public void Remove(PlayerInjury injury) => db.PlayerInjuries.Remove(injury);
}
