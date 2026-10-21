namespace TercerTiempo.Application.Abstractions.FootballData;

/// <summary>
/// Fuente externa de datos de las competiciones (API REST de futbolasturiano.es, ficheros JSON, datos ficticios...).
/// Cada implementación decide qué ligas publica y qué temporada sigue; los identificadores externos son suyos.
/// </summary>
public interface IFootballDataProvider
{
    string ProviderName { get; }

    /// <summary>Ligas disponibles (una por categoría y grupo) con su temporada en curso.</summary>
    Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken);
    /// <summary>
    /// Calendario y resultados de la liga. <paramref name="known"/> son los partidos de esa liga que ya están guardados
    /// (como instantáneas del mismo proveedor): un proveedor caro de consultar puede reutilizarlos en lugar de volver a
    /// descargarlos. Los demás pueden ignorarlos.
    /// </summary>
    Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(
        string competitionExternalId,
        IReadOnlyCollection<MatchSnapshot> known,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken);

    /// <summary>Plantilla publicada de un equipo; lista vacía si el proveedor no la publica.</summary>
    Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken);
}
