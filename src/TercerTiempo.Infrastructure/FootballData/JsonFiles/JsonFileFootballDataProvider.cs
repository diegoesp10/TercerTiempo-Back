using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;

namespace TercerTiempo.Infrastructure.FootballData.JsonFiles;

/// <summary>
/// Proveedor que lee instantáneas desde ficheros JSON locales. Útil para volcar el resultado de un scraper
/// externo (Python, Playwright, ...) sin acoplarlo a la API, y para pruebas. Estructura esperada:
///   competitions.json                                  → CompetitionSnapshot[]
///   competitions/{competitionExternalId}/teams.json     → TeamSnapshot[]
///   competitions/{competitionExternalId}/matches.json   → MatchSnapshot[]
///   competitions/{competitionExternalId}/standings.json → StandingSnapshot[]
///   rosters/{teamExternalId}.json                       → RosterEntrySnapshot[]
/// </summary>
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

    public async Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(string competitionExternalId, CancellationToken cancellationToken) =>
        await ReadAsync<List<MatchSnapshot>>(Path.Combine("competitions", competitionExternalId, "matches.json"), cancellationToken) ?? [];

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
}
