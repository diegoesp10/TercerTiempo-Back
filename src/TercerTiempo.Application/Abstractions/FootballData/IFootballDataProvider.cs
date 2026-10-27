namespace TercerTiempo.Application.Abstractions.FootballData;

public interface IFootballDataProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken);
    // Con window, solo los partidos de esos días (en la hora local de la web); sin ella, la temporada entera.
    Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(
        string competitionExternalId,
        IReadOnlyCollection<MatchSnapshot> known,
        MatchWindow? window,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken);

    // Puestos destacados de la clasificación (ascenso, promoción, descenso...) de la temporada en curso de la liga: vacío si
    // la web no los marca, null si no se han podido leer.
    Task<IReadOnlyList<TercerTiempo.Domain.Competitions.LeagueZoneData>?> GetLeagueZonesAsync(string competitionExternalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken);

    // Equipo arbitral de un partido según la web del proveedor: vacío si no lo publica, null si no se ha podido leer.
    Task<IReadOnlyList<TercerTiempo.Domain.Referees.MatchOfficialData>?> GetMatchOfficialsAsync(string matchExternalId, CancellationToken cancellationToken);
}
