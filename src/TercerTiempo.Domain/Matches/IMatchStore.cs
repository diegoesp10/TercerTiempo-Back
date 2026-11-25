namespace TercerTiempo.Domain.Matches;

public interface IMatchStore
{
    Task<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    // Los partidos de varias temporadas a la vez, solo para leer (sin seguimiento), en el orden de GetBySeasonAsync.
    Task<IReadOnlyList<Match>> GetBySeasonsAsync(IEnumerable<Guid> seasonIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Match>> GetTeamMatchesAsync(Guid seasonId, Guid teamId, CancellationToken cancellationToken = default);

    // Todos los partidos entre dos equipos, de cualquier temporada y liga, solo para leer (del más antiguo al más reciente).
    Task<IReadOnlyList<Match>> GetBetweenAsync(Guid teamA, Guid teamB, CancellationToken cancellationToken = default);

    // Los programados (sin jugar ni aplazar) con el saque después de after y no más tarde de until, de todas las ligas,
    // solo para leer.
    Task<IReadOnlyList<Match>> GetScheduledKickingOffAsync(DateTimeOffset after, DateTimeOffset until, CancellationToken cancellationToken = default);
    void Add(Match match);
}
