using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;

namespace TercerTiempo.Infrastructure.FootballData.JsonFiles;

internal sealed class JsonFileFootballDataProvider(IOptions<FootballDataOptions> options) : IFootballDataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ProviderName => FootballDataOptions.JsonFilesProvider;

    private string Directory => options.Value.JsonFiles.Directory;

    public async Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken) =>
        await ReadAsync<List<CompetitionSnapshot>>("competitions.json", cancellationToken)
        ?? throw new InvalidOperationException($"No se encontró competitions.json en '{Directory}'.");

    public async Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken) =>
        await ReadAsync<List<TeamSnapshot>>(Path.Combine("competitions", competitionExternalId, "teams.json"), cancellationToken) ?? [];

    public async Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(
        string competitionExternalId, IReadOnlyCollection<MatchSnapshot> known, MatchWindow? window, CancellationToken cancellationToken) =>
        (await ReadAsync<List<MatchSnapshot>>(Path.Combine("competitions", competitionExternalId, "matches.json"), cancellationToken) ?? [])
            .Where(m => window is null || window.Contains(DateOnly.FromDateTime(m.KickoffAt.UtcDateTime)))
            .ToList();

    public async Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken) =>
        await ReadAsync<List<StandingSnapshot>>(Path.Combine("competitions", competitionExternalId, "standings.json"), cancellationToken) ?? [];

    public async Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken) =>
        await ReadAsync<List<RosterEntrySnapshot>>(Path.Combine("rosters", $"{teamExternalId}.json"), cancellationToken) ?? [];

    private async Task<T?> ReadAsync<T>(string relativePath, CancellationToken cancellationToken) where T : class
    {
        var path = Path.Combine(Directory, relativePath);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    // Los datos de prueba no traen equipo arbitral.
    public Task<IReadOnlyList<TercerTiempo.Domain.Referees.MatchOfficialData>?> GetMatchOfficialsAsync(string matchExternalId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TercerTiempo.Domain.Referees.MatchOfficialData>?>([]);
}
