namespace TercerTiempo.Domain.Coaches;

public interface ICoachStore
{
    Task<Coach?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Coach?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Coach>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    void Add(Coach coach);
}
