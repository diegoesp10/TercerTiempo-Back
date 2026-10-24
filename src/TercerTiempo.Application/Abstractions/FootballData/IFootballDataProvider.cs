namespace TercerTiempo.Application.Abstractions.FootballData;

public interface IFootballDataProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(
        string competitionExternalId,
        IReadOnlyCollection<MatchSnapshot> known,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken);
}
