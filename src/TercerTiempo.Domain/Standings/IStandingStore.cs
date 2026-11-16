namespace TercerTiempo.Domain.Standings;

public interface IStandingStore
{
    Task<IReadOnlyList<Standing>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<Standing?> GetByTeamAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default);

    // Filas de clasificación de cada temporada (las que no tienen ninguna no salen).
    Task<IReadOnlyDictionary<Guid, int>> CountBySeasonsAsync(IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default);
    void Add(Standing standing);
}
