namespace TercerTiempo.Domain.Standings;

public interface IStandingStore
{
    Task<IReadOnlyList<Standing>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    void Add(Standing standing);
}
