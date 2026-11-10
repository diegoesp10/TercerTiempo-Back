namespace TercerTiempo.Domain.Matches;

public interface IMatchStore
{
    Task<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetTeamMatchesAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default);
    void Add(Match match);
}
