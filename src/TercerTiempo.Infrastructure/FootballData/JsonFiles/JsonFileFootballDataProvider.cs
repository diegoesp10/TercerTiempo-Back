using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;

namespace TercerTiempo.Infrastructure.FootballData.JsonFiles;

/// <summary>
/// Proveedor que lee instantáneas desde ficheros JSON locales. Útil para volcar el resultado de
/// un scraper externo (Python, Playwright, ...) sin acoplarlo a la API, y para pruebas.
/// Estructura esperada en la carpeta configurada:
///   competition.json, teams.json, matches.json, standings.json, rosters/{teamExternalId}.json
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

    public async Task<CompetitionSeasonSnapshot> GetCompetitionSeasonAsync(CancellationToken cancellationToken) =>
        await ReadAsync<CompetitionSeasonSnapshot>("competition.json", cancellationToken)
        ?? throw new InvalidOperationException($"No se encontró competition.json en '{Directory}'.");

    public async Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(CancellationToken cancellationToken) =>
        await ReadAsync<List<TeamSnapshot>>("teams.json", cancellationToken) ?? [];

    public async Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(CancellationToken cancellationToken) =>
        await ReadAsync<List<MatchSnapshot>>("matches.json", cancellationToken) ?? [];

    public async Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken) =>
        await ReadAsync<List<StandingSnapshot>>("standings.json", cancellationToken) ?? [];

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
