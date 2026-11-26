namespace TercerTiempo.Domain.Performances;

public interface IPerformanceStore
{
    Task<PlayerMatchPerformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadPlayerAsync(Guid squadPlayerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySquadAndMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerMatchPerformance>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // Lo de cada jugador en la temporada que entra en las medias de la liga: los de esa plantilla y los de las plantillas
    // públicas. Sumado en la base (a final de temporada son miles de líneas; los jugadores, unos cientos).
    Task<IReadOnlyList<PlayerSeasonTotals>> GetComparableTotalsBySeasonAsync(Guid seasonId, Guid ownSquadId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerMatchPerformance>> GetWithoutRatingAsync(CancellationToken cancellationToken = default);

    // Las que tienen acta oficial cerrada en su partido o ya tomaron algo de ella (puede haber dejado de estar).
    Task<IReadOnlyList<PlayerMatchPerformance>> GetWithOfficialSheetDataAsync(CancellationToken cancellationToken = default);
    void Add(PlayerMatchPerformance performance);
    void Remove(PlayerMatchPerformance performance);
}

// AverageRating: la media de las notas que hay (sin nota no cuenta), null si no tiene ninguna.
public sealed record PlayerSeasonTotals(Guid SquadPlayerId, int Minutes, int Goals, int Assists, double? AverageRating);
