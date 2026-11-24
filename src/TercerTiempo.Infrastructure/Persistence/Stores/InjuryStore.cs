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

    public async Task<IReadOnlyList<PlayerInjury>> GetBySquadPlayersAsync(IReadOnlyCollection<Guid> squadPlayerIds, CancellationToken cancellationToken = default)
    {
        if (squadPlayerIds.Count == 0) return [];

        return await db.PlayerInjuries
            .Where(i => squadPlayerIds.Contains(i.SquadPlayerId))
            .OrderByDescending(i => i.StartedOn)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerInjury>> GetOpenAsync(IReadOnlyCollection<Guid>? squadIds = null, CancellationToken cancellationToken = default)
    {
        if (squadIds is { Count: 0 }) return [];

        var open = db.PlayerInjuries.Where(i => i.ReturnedOn == null && i.ContinuedAt == null);
        if (squadIds is not null) open = open.Where(i => squadIds.Contains(i.SquadId));
        return await open.OrderBy(i => i.StartedOn).ToListAsync(cancellationToken);
    }

    public Task<PlayerInjury?> GetContinuationAsync(Guid injuryId, CancellationToken cancellationToken = default) =>
        db.PlayerInjuries.FirstOrDefaultAsync(i => i.ContinuesInjuryId == injuryId, cancellationToken);

    public async Task<IReadOnlyList<PlayerInjury>> GetByTrainingSessionAsync(Guid trainingSessionId, CancellationToken cancellationToken = default) =>
        await db.PlayerInjuries.Where(i => i.TrainingSessionId == trainingSessionId).ToListAsync(cancellationToken);

    public void Add(PlayerInjury injury) => db.PlayerInjuries.Add(injury);

    public void Remove(PlayerInjury injury) => db.PlayerInjuries.Remove(injury);
}
