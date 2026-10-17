using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class CompetitionStore(AppDbContext db) : ICompetitionStore
{
    public Task<Competition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Competition?> GetByExternalRefAsync(ExternalRef externalRef, CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(
            c => c.ExternalRef!.Provider == externalRef.Provider && c.ExternalRef.ExternalId == externalRef.ExternalId,
            cancellationToken);

    public Task<Competition?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(c => c.Seasons.Any(s => s.IsCurrent), cancellationToken);

    public Task<Competition?> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        db.Competitions.FirstOrDefaultAsync(c => c.Seasons.Any(s => s.Id == seasonId), cancellationToken);

    public void Add(Competition competition) => db.Competitions.Add(competition);
}
