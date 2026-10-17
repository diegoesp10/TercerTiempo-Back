using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

/// <summary>
/// Proveedor real: lee la API REST pública de futbolasturiano.es (WordPress + SportsPress).
///  - Ligas y grupos: taxonomía <c>sp_league</c>; temporadas: <c>sp_season</c>.
///  - Equipos: <c>sp_team</c> filtrado por liga y temporada (con escudo embebido), más los que aparezcan
///    en el calendario sin estar etiquetados en la liga.
///  - Partidos: <c>sp_event</c> filtrado por liga y temporada (jornada en <c>day</c>, goles en <c>results</c>).
///  - Clasificación: <c>sportspress/v2/sp_table</c> (datos por id de equipo).
///  - Plantillas: catálogo completo de <c>sp_player</c> agrupado por <c>current_teams</c>
///    (la API no permite filtrarlos por equipo; se descarga una vez por sincronización).
/// Las respuestas se cachean durante la vida de la instancia (una por sincronización).
/// </summary>
internal sealed class FutbolAsturianoFootballDataProvider(
    HttpClient http,
    IOptions<FootballDataOptions> options,
    ILogger<FutbolAsturianoFootballDataProvider> logger) : IFootballDataProvider
{
    private const int MaxRetries = 3;
    private const string TeamFields = "_fields=id,title,abbreviation,featured_media,_embedded,_links&_embed=wp:featuredmedia";

    private readonly FutbolAsturianoOptions _settings = options.Value.FutbolAsturiano;
    private readonly Dictionary<string, List<JsonElement>> _eventsByLeague = [];
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    private (int Id, string Label)? _season;
    private Dictionary<int, string>? _venues;
    private Dictionary<int, Position?>? _positions;
    private List<JsonElement>? _tables;
    private ILookup<int, JsonElement>? _playersByTeam;

    public string ProviderName => FootballDataOptions.FutbolAsturianoProvider;

    public async Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken)
    {
        var season = await ResolveSeasonAsync(cancellationToken);
        var (startsOn, endsOn) = SeasonDates(season.Label);

        var leagues = await GetPagedAsync("wp/v2/sp_league?_fields=id,name,slug", cancellationToken);
        var result = new List<CompetitionSnapshot>();

        foreach (var league in leagues)
        {
            var id = Json.GetInt(league, "id") ?? 0;
            var name = Json.GetString(league, "name") ?? string.Empty;
            if (id == 0 || name.Length == 0) continue;

            var info = LeagueClassifier.Classify(name);
            if (!IsIncluded(id, info.Level)) continue;

            result.Add(new CompetitionSnapshot(
                id.ToString(CultureInfo.InvariantCulture), info.DisplayName, info.Category, info.Level, info.Tier, info.Group,
                _settings.Region, season.Id.ToString(CultureInfo.InvariantCulture), season.Label, startsOn, endsOn));
        }

        logger.LogInformation("futbolasturiano.es: {Count} ligas seleccionadas de {Total} para la temporada {Season}",
            result.Count, leagues.Count, season.Label);

        return result;
    }

    public async Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        var season = await ResolveSeasonAsync(cancellationToken);
        var items = await GetPagedAsync(
            $"wp/v2/sp_team?sp_league={competitionExternalId}&sp_season={season.Id}&{TeamFields}",
            cancellationToken);

        var teams = items.Select(ToTeam).OfType<TeamSnapshot>().ToList();

        // Equipos que juegan partidos de la liga pero que la web no ha etiquetado en ella: se recuperan por id.
        var known = teams.Select(t => t.ExternalId).ToHashSet();
        var missing = (await GetEventsAsync(competitionExternalId, cancellationToken))
            .SelectMany(e => Json.GetIntArray(e, "teams"))
            .Select(id => id.ToString(CultureInfo.InvariantCulture))
            .Where(id => !known.Contains(id))
            .Distinct()
            .ToList();

        if (missing.Count > 0)
        {
            logger.LogInformation("futbolasturiano.es: {Count} equipos de la liga {League} no están etiquetados en ella; se recuperan por id ({Ids})",
                missing.Count, competitionExternalId, string.Join(",", missing));

            foreach (var chunk in missing.Chunk(_settings.PageSize))
            {
                var extra = await GetPagedAsync($"wp/v2/sp_team?include={string.Join(',', chunk)}&{TeamFields}", cancellationToken);
                teams.AddRange(extra.Select(ToTeam).OfType<TeamSnapshot>());
            }
        }

        return teams;
    }

    public async Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        var venues = await GetVenuesAsync(cancellationToken);
        var items = await GetEventsAsync(competitionExternalId, cancellationToken);

        var matches = new List<MatchSnapshot>(items.Count);

        foreach (var item in items)
        {
            var id = Json.GetInt(item, "id");
            var teams = Json.GetIntArray(item, "teams");
            var matchday = Json.GetInt(item, "day");
            var kickoff = Json.GetUtcDate(item, "date_gmt");

            if (id is null || teams.Count < 2 || matchday is null or < 1 || kickoff is null)
            {
                logger.LogDebug("Evento {Id} ignorado: datos incompletos (equipos={Teams}, jornada={Day})", id, teams.Count, matchday);
                continue;
            }

            var home = teams[0];
            var away = teams[1];
            var homeGoals = Json.GetGoals(item, home);
            var awayGoals = Json.GetGoals(item, away);
            var played = homeGoals is not null && awayGoals is not null;

            var venueIds = Json.GetIntArray(item, "sp_venue");
            var venue = venueIds.Count > 0 && venues.TryGetValue(venueIds[0], out var venueName) ? venueName : null;

            matches.Add(new MatchSnapshot(
                id.Value.ToString(CultureInfo.InvariantCulture),
                matchday.Value,
                home.ToString(CultureInfo.InvariantCulture),
                away.ToString(CultureInfo.InvariantCulture),
                kickoff.Value,
                played ? MatchStatus.Played : MatchStatus.Scheduled,
                played ? homeGoals : null,
                played ? awayGoals : null,
                venue));
        }

        return matches;
    }

    public async Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        var season = await ResolveSeasonAsync(cancellationToken);
        var leagueId = int.Parse(competitionExternalId, CultureInfo.InvariantCulture);

        _tables ??= await GetPagedAsync("sportspress/v2/sp_table?_fields=id,title,modified,sp_league,sp_season,data", cancellationToken);

        var table = _tables
            .Where(t => Json.GetIntArray(t, "sp_league").Contains(leagueId) && Json.GetIntArray(t, "sp_season").Contains(season.Id))
            .OrderByDescending(t => Json.GetString(t, "modified"))
            .FirstOrDefault();

        if (table.ValueKind != JsonValueKind.Object || !table.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            logger.LogWarning("futbolasturiano.es: sin clasificación publicada para la liga {League} ({Season})", competitionExternalId, season.Label);
            return [];
        }

        var rows = new List<StandingSnapshot>();

        foreach (var row in data.EnumerateObject())
        {
            if (!int.TryParse(row.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamId) || teamId == 0)
                continue; // "0" es la fila de cabeceras

            if (row.Value.ValueKind != JsonValueKind.Object) continue;

            var position = Json.GetInt(row.Value, "pos");
            if (position is null or < 1) continue;

            rows.Add(new StandingSnapshot(
                teamId.ToString(CultureInfo.InvariantCulture),
                position.Value,
                Json.GetInt(row.Value, "p") ?? 0,
                Json.GetInt(row.Value, "w") ?? 0,
                Json.GetInt(row.Value, "d") ?? 0,
                Json.GetInt(row.Value, "l") ?? 0,
                Json.GetInt(row.Value, "f") ?? 0,
                Json.GetInt(row.Value, "a") ?? 0,
                Json.GetInt(row.Value, "pts") ?? 0));
        }

        return rows.OrderBy(r => r.Position).ToList();
    }

    public async Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken)
    {
        var teamId = int.Parse(teamExternalId, CultureInfo.InvariantCulture);
        var players = await GetPlayersByTeamAsync(cancellationToken);
        var positions = await GetPositionsAsync(cancellationToken);

        var roster = new List<RosterEntrySnapshot>();

        foreach (var player in players[teamId])
        {
            var name = Json.GetRenderedTitle(player);
            if (name.Length == 0) continue;

            var positionIds = Json.GetIntArray(player, "sp_position");
            var position = positionIds.Count > 0 && positions.TryGetValue(positionIds[0], out var mapped) ? mapped : null;
            var number = Json.GetInt(player, "number");

            roster.Add(new RosterEntrySnapshot(
                Json.GetInt(player, "id")?.ToString(CultureInfo.InvariantCulture),
                name,
                position,
                number is > 0 and < 100 ? number : null));
        }

        return roster.OrderBy(r => r.ShirtNumber ?? int.MaxValue).ThenBy(r => r.FullName).ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Catálogos cacheados por sincronización
    // ---------------------------------------------------------------------------------------------

    private static TeamSnapshot? ToTeam(JsonElement item)
    {
        var id = Json.GetInt(item, "id");
        var name = Json.GetRenderedTitle(item);
        if (id is null || name.Length == 0) return null;

        return new TeamSnapshot(
            id.Value.ToString(CultureInfo.InvariantCulture),
            name,
            TeamNames.ShortName(name, Json.GetString(item, "abbreviation")),
            Json.GetCrestUrl(item));
    }

    private async Task<List<JsonElement>> GetEventsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        if (_eventsByLeague.TryGetValue(competitionExternalId, out var cached)) return cached;

        var season = await ResolveSeasonAsync(cancellationToken);
        var events = await GetPagedAsync(
            $"wp/v2/sp_event?sp_league={competitionExternalId}&sp_season={season.Id}&orderby=date&order=asc&_fields=id,date_gmt,day,teams,results,sp_venue",
            cancellationToken);

        _eventsByLeague[competitionExternalId] = events;
        return events;
    }

    private async Task<(int Id, string Label)> ResolveSeasonAsync(CancellationToken cancellationToken)
    {
        if (_season is { } cached) return cached;

        var seasons = await GetPagedAsync("wp/v2/sp_season?hide_empty=false&_fields=id,name", cancellationToken);
        var candidates = seasons
            .Select(s => (Id: Json.GetInt(s, "id") ?? 0, Label: Json.GetString(s, "name") ?? string.Empty))
            .Where(s => s.Id > 0 && s.Label.Length > 0)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException("futbolasturiano.es no publica ninguna temporada (sp_season).");

        var chosen = string.IsNullOrWhiteSpace(_settings.Season)
            ? candidates.OrderByDescending(s => s.Label, StringComparer.Ordinal).First()
            : candidates.FirstOrDefault(s => s.Label.Equals(_settings.Season.Trim(), StringComparison.OrdinalIgnoreCase));

        if (chosen.Id == 0)
            throw new InvalidOperationException(
                $"La temporada '{_settings.Season}' no existe en futbolasturiano.es. Disponibles: {string.Join(", ", candidates.Select(c => c.Label))}");

        _season = chosen;
        return chosen;
    }

    private async Task<Dictionary<int, string>> GetVenuesAsync(CancellationToken cancellationToken)
    {
        if (_venues is not null) return _venues;

        var venues = await GetPagedAsync("wp/v2/sp_venue?hide_empty=false&_fields=id,name", cancellationToken);
        _venues = venues
            .Select(v => (Id: Json.GetInt(v, "id") ?? 0, Name: WebUtility.HtmlDecode(Json.GetString(v, "name") ?? string.Empty)))
            .Where(v => v.Id > 0 && v.Name.Length > 0)
            .ToDictionary(v => v.Id, v => v.Name);

        return _venues;
    }

    private async Task<Dictionary<int, Position?>> GetPositionsAsync(CancellationToken cancellationToken)
    {
        if (_positions is not null) return _positions;

        var positions = await GetPagedAsync("wp/v2/sp_position?hide_empty=false&_fields=id,name", cancellationToken);
        _positions = positions
            .Select(p => (Id: Json.GetInt(p, "id") ?? 0, Name: Json.GetString(p, "name") ?? string.Empty))
            .Where(p => p.Id > 0)
            .ToDictionary(p => p.Id, p => MapPosition(p.Name));

        return _positions;
    }

    private async Task<ILookup<int, JsonElement>> GetPlayersByTeamAsync(CancellationToken cancellationToken)
    {
        if (_playersByTeam is not null) return _playersByTeam;

        var players = await GetPagedAsync("wp/v2/sp_player?_fields=id,title,number,sp_position,current_teams", cancellationToken);

        _playersByTeam = players
            .SelectMany(p => Json.GetIntArray(p, "current_teams").Select(teamId => (TeamId: teamId, Player: p)))
            .ToLookup(x => x.TeamId, x => x.Player);

        logger.LogInformation("futbolasturiano.es: catálogo de jugadores descargado ({Count} fichas)", players.Count);
        return _playersByTeam;
    }

    private bool IsIncluded(int leagueId, CompetitionLevel level)
    {
        if (_settings.ExcludeLeagueIds.Contains(leagueId)) return false;
        if (_settings.IncludeLeagueIds.Contains(leagueId)) return true;

        return level switch
        {
            CompetitionLevel.Professional => _settings.IncludeProfessional,
            CompetitionLevel.National => _settings.IncludeNational,
            CompetitionLevel.Regional => _settings.IncludeRegional,
            CompetitionLevel.Cup => _settings.IncludeCups,
            CompetitionLevel.Youth => _settings.IncludeYouth,
            _ => false,
        };
    }

    private (DateOnly StartsOn, DateOnly EndsOn) SeasonDates(string label)
    {
        var parts = label.Split('/', '-');
        var startYear = parts.Length > 0 && int.TryParse(parts[0], out var y) ? y : DateTime.UtcNow.Year;
        var endYear = parts.Length > 1 && int.TryParse(parts[1], out var y2) ? (y2 < 100 ? 2000 + y2 : y2) : startYear + 1;

        var startMonth = Math.Clamp(_settings.SeasonStartMonth, 1, 12);
        var endMonth = Math.Clamp(_settings.SeasonEndMonth, 1, 12);

        return (new DateOnly(startYear, startMonth, 1), new DateOnly(endYear, endMonth, DateTime.DaysInMonth(endYear, endMonth)));
    }

    private static Position? MapPosition(string name)
    {
        var n = name.ToLowerInvariant();

        if (n.Contains("portero") || n.Contains("guardameta")) return Position.Goalkeeper;
        if (n.Contains("defensa") || n.Contains("lateral") || n.Contains("central") || n.Contains("carrilero")) return Position.Defender;
        if (n.Contains("medio") || n.Contains("centrocampista") || n.Contains("interior") || n.Contains("pivote") || n.Contains("volante")) return Position.Midfielder;
        if (n.Contains("delantero") || n.Contains("extremo") || n.Contains("punta") || n.Contains("ariete")) return Position.Forward;

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // HTTP: paginación de la API REST de WordPress (cabeceras X-WP-TotalPages), ritmo y reintentos
    // ---------------------------------------------------------------------------------------------

    private async Task<List<JsonElement>> GetPagedAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        var separator = pathAndQuery.Contains('?') ? '&' : '?';
        var page = 1;
        int totalPages;

        do
        {
            using var response = await SendAsync($"{pathAndQuery}{separator}per_page={_settings.PageSize}&page={page}", cancellationToken);

            totalPages = response.Headers.TryGetValues("X-WP-TotalPages", out var values)
                && int.TryParse(values.FirstOrDefault(), out var parsed)
                ? parsed
                : 1;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
                items.AddRange(document.RootElement.EnumerateArray().Select(e => e.Clone()));

            page++;
        }
        while (page <= totalPages);

        return items;
    }

    private async Task<HttpResponseMessage> SendAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(cancellationToken);

            HttpResponseMessage? response = null;
            try
            {
                response = await http.GetAsync(pathAndQuery, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return response;

                var status = (int)response.StatusCode;
                response.Dispose();

                if (attempt >= MaxRetries || status is >= 400 and < 500 and not 429)
                    throw new HttpRequestException($"futbolasturiano.es respondió {status} a {pathAndQuery}");

                logger.LogWarning("futbolasturiano.es respondió {Status} a {Path}; reintento {Attempt}/{Max}", status, pathAndQuery, attempt, MaxRetries);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                response?.Dispose();
                logger.LogWarning("Error de red con futbolasturiano.es en {Path}; reintento {Attempt}/{Max}", pathAndQuery, attempt, MaxRetries);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries)
            {
                response?.Dispose();
                logger.LogWarning("Timeout con futbolasturiano.es en {Path}; reintento {Attempt}/{Max}", pathAndQuery, attempt, MaxRetries);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        var wait = TimeSpan.FromMilliseconds(_settings.RequestDelayMs) - (DateTimeOffset.UtcNow - _lastRequestAt);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
    }
}
