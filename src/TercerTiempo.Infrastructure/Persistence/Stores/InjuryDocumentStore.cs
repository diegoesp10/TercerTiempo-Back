using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class InjuryDocumentStore(AppDbContext db) : IInjuryDocumentStore
{
    public Task<InjuryDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.InjuryDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InjuryDocument>> GetByInjuryIdsAsync(IReadOnlyCollection<Guid> injuryIds, CancellationToken cancellationToken = default) =>
        injuryIds.Count == 0
            ? []
            : await db.InjuryDocuments
                .Where(d => injuryIds.Contains(d.InjuryId))
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync(cancellationToken);

    public void Add(InjuryDocument document) => db.InjuryDocuments.Add(document);

    public void Remove(InjuryDocument document) => db.InjuryDocuments.Remove(document);
}
