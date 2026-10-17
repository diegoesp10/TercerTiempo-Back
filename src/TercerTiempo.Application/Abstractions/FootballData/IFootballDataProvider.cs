namespace TercerTiempo.Application.Abstractions.FootballData;

/// <summary>
/// Fuente externa de datos de la competición (API de BeSoccer, scraping de FutbolAsturiano, ...).
/// Cada implementación viene configurada con la competición/temporada que sigue.
/// </summary>
public interface IFootballDataProvider
{
    string ProviderName { get; }

    Task<CompetitionSeasonSnapshot> GetCompetitionSeasonAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken);
}
