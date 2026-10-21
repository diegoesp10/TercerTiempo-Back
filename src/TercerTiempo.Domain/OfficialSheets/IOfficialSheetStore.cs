namespace TercerTiempo.Domain.OfficialSheets;

public interface IOfficialSheetStore
{
    Task<OfficialMatchSheet?> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfficialMatchSheet>> GetByMatchIdsAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    void Add(OfficialMatchSheet sheet);
}
