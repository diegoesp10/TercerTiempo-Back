namespace TercerTiempo.Domain.Squads;

public interface IInjuryDocumentStore
{
    Task<InjuryDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryDocument>> GetByInjuryIdsAsync(IReadOnlyCollection<Guid> injuryIds, CancellationToken cancellationToken = default);
    void Add(InjuryDocument document);
    void Remove(InjuryDocument document);
}
