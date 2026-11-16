namespace TercerTiempo.Domain.Matches;

public interface IMatchStore
{
    Task<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    // Los partidos de varias temporadas a la vez, solo para leer (sin seguimiento), en el orden de GetBySeasonAsync.
    Task<IReadOnlyList<Match>> GetBySeasonsAsync(IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetTeamMatchesAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default);
    void Add(Match match);
}
