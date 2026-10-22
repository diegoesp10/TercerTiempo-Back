using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Infrastructure.FootballData.BeSoccer;

internal sealed class BeSoccerFootballDataProvider(
    HttpClient http,
    IOptions<FootballDataOptions> options,
    ILogger<BeSoccerFootballDataProvider> logger) : IFootballDataProvider
{
    public string ProviderName => FootballDataOptions.BeSoccerProvider;

    private BeSoccerOptions Settings => options.Value.BeSoccer;

    public Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        // La competición/temporada se define por configuración: la API solo aporta equipos, partidos y clasificación.
        IReadOnlyList<CompetitionSnapshot> competitions =
        [
            new(Settings.CompetitionId, Settings.CompetitionName, Settings.CompetitionName, Settings.Level, Settings.Tier, Settings.Group,
                Settings.Region, Settings.Season, Settings.SeasonLabel, Settings.SeasonStartsOn, Settings.SeasonEndsOn),
        ];

        return Task.FromResult(competitions);
    }

    public async Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync("teams", cancellationToken);
        if (!TryGetArray(document, "team", out var teams)) return [];

        return teams
            .Select(t => new TeamSnapshot(
                GetString(t, "id") ?? string.Empty,
                GetString(t, "nameShow") ?? GetString(t, "name") ?? string.Empty,
                GetString(t, "short_name") ?? GetString(t, "nameShow") ?? string.Empty,
                GetString(t, "shield")))
            .Where(t => t.ExternalId.Length > 0 && t.Name.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(string competitionExternalId, IReadOnlyCollection<MatchSnapshot> known, CancellationToken cancellationToken)
    {
        using var document = await GetAsync("matchs", cancellationToken);
        if (!TryGetArray(document, "match", out var matches)) return [];

        var result = new List<MatchSnapshot>();

        foreach (var m in matches)
        {
            var id = GetString(m, "id");
            var home = GetString(m, "local_id");
            var away = GetString(m, "visitor_id");
            if (id is null || home is null || away is null) continue;

            var kickoff = ParseDate(GetString(m, "schedule") ?? GetString(m, "date"));
            var status = GetInt(m, "status") switch
            {
                1 => MatchStatus.Played,
                0 => MatchStatus.InProgress,
                _ => MatchStatus.Scheduled,
            };

            result.Add(new MatchSnapshot(
                id,
                GetInt(m, "round") ?? 1,
                home,
                away,
                kickoff,
                status,
                status == MatchStatus.Played ? GetInt(m, "local_goals") : null,
                status == MatchStatus.Played ? GetInt(m, "visitor_goals") : null,
                GetString(m, "stadium")));
        }

        return result;
    }

    public async Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync("tables", cancellationToken);
        if (!TryGetArray(document, "table", out var rows)) return [];

        return rows
            .Select(r => new StandingSnapshot(
                GetString(r, "id") ?? string.Empty,
                GetInt(r, "pos") ?? 0,
                GetInt(r, "played") ?? 0,
                GetInt(r, "wins") ?? 0,
                GetInt(r, "draws") ?? 0,
                GetInt(r, "losses") ?? 0,
                GetInt(r, "gf") ?? 0,
                GetInt(r, "ga") ?? 0,
                GetInt(r, "points") ?? 0))
            .Where(s => s.TeamExternalId.Length > 0 && s.Position > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync("team_players", cancellationToken, ("team", teamExternalId));
        if (!TryGetArray(document, "players", out var players)) return [];

        return players
            .Select(p => new RosterEntrySnapshot(
                GetString(p, "id"),
                GetString(p, "nick") ?? GetString(p, "name") ?? string.Empty,
                MapPosition(GetString(p, "role")),
                GetInt(p, "squad_number")))
            .Where(p => p.FullName.Length > 0)
            .ToList();
    }

    private async Task<JsonDocument> GetAsync(string request, CancellationToken cancellationToken, params (string Key, string Value)[] extra)
    {
        EnsureConfigured();

        var query = new List<string>
        {
            $"key={Uri.EscapeDataString(Settings.ApiKey)}",
            "format=json",
            $"req={request}",
            $"league={Uri.EscapeDataString(Settings.CompetitionId)}",
            $"year={Uri.EscapeDataString(Settings.Season)}",
        };
        query.AddRange(extra.Select(e => $"{e.Key}={Uri.EscapeDataString(e.Value)}"));

        var url = $"{Settings.BaseUrl}?{string.Join('&', query)}";
        logger.LogDebug("BeSoccer GET req={Request}", request);

        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey) || string.IsNullOrWhiteSpace(Settings.CompetitionId))
            throw new InvalidOperationException("Configura FootballData:BeSoccer:ApiKey y CompetitionId para usar este proveedor.");
    }

    private static bool TryGetArray(JsonDocument document, string property, out IEnumerable<JsonElement> items)
    {
        if (document.RootElement.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array)
        {
            items = element.EnumerateArray();
            return true;
        }

        items = [];
        return false;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        int.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : DateTimeOffset.MinValue;

    private static Position? MapPosition(string? role) => role?.ToLowerInvariant() switch
    {
        "1" or "por" or "gk" or "portero" => Position.Goalkeeper,
        "2" or "def" or "defensa" => Position.Defender,
        "3" or "cen" or "mid" or "centrocampista" => Position.Midfielder,
        "4" or "del" or "fw" or "delantero" => Position.Forward,
        _ => null,
    };
}
