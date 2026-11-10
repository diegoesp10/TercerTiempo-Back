namespace TercerTiempo.Domain.OfficialSheets;

public interface IOfficialSheetStore
{
    Task<OfficialMatchSheet?> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficialMatchSheet>> GetByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);

    // Las dos anteriores no devuelven las actas sacadas de webs que ya no están en OfficialSheets:Sources (se ocultan,
    // no se borran). Estas sí, para que la descarga pueda sustituirlas por la de una web activa.
    Task<OfficialMatchSheet?> GetStoredByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficialMatchSheet>> GetStoredByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    void Add(OfficialMatchSheet sheet);
}
