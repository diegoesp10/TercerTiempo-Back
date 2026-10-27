using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Referees;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

internal sealed class FutbolAsturianoFootballDataProvider(
    HttpClient http,
    IOptions<FootballDataOptions> options,
    TimeProvider clock,
    ILogger<FutbolAsturianoFootballDataProvider> logger) : IFootballDataProvider
{
    private const int MaxRetries = 3;
    private const int MaxBlockExtensions = 20;
    private const string TeamFields = "_fields=id,slug,title,abbreviation,featured_media,_embedded,_links&_embed=wp:featuredmedia";

    private static readonly TimeSpan RefreshBehind = TimeSpan.FromDays(2);

    private static readonly TimeSpan LiveWindow = TimeSpan.FromHours(3);

    // En la jornada, un partido oculto solo se relee cuando ya ha empezado (o empieza en estas horas) y aún no tiene
    // resultado: releer cada hora los de dentro de varios días no aporta nada (la pasada completa corrige sus fechas).
    private static readonly TimeSpan HiddenRoundLookAhead = TimeSpan.FromHours(6);

    private readonly FutbolAsturianoOptions _settings = options.Value.FutbolAsturiano;
    private readonly Dictionary<string, Paged> _eventsByLeague = [];
    private readonly Dictionary<string, List<JsonElement>> _teamsByLeague = [];
    private readonly Dictionary<string, List<TeamSnapshot>> _extraTeamsByLeague = [];
    private readonly Dictionary<string, int> _teamIdBySlug = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unknownSlugs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, EventPage?> _eventPages = [];
    private readonly Dictionary<string, string> _leagueNames = [];
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    private MatchWindow? _window;

    private (int Id, string Label)? _season;
    private TimeZoneInfo? _timeZone;
    private Dictionary<int, string>? _venues;
    private Dictionary<int, Position?>? _positions;
    private List<JsonElement>? _tables;
    private ILookup<int, JsonElement>? _playersByTeam;

    public string ProviderName => FootballDataOptions.FutbolAsturianoProvider;

    private TimeZoneInfo SiteTimeZone => _timeZone ??= SiteClock.Resolve(_settings.TimeZone);

    public async Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken)
    {
        var season = await ResolveSeasonAsync(cancellationToken);
        var (startsOn, endsOn) = SeasonDates(season.Label);

        var leagues = (await GetPagedAsync("wp/v2/sp_league?_fields=id,name,slug", cancellationToken)).Items;
        var result = new List<CompetitionSnapshot>();

        foreach (var league in leagues)
        {
            var id = Json.GetInt(league, "id") ?? 0;
            var name = Json.GetString(league, "name") ?? string.Empty;
            if (id == 0 || name.Length == 0) continue;

            var externalId = id.ToString(CultureInfo.InvariantCulture);
            _leagueNames[externalId] = WebUtility.HtmlDecode(name);

            var info = LeagueClassifier.Classify(name);
            if (!IsIncluded(id, info.Level)) continue;

            result.Add(new CompetitionSnapshot(
                externalId, info.DisplayName, info.Category, info.Level, info.Tier, info.Group,
                _settings.Region, season.Id.ToString(CultureInfo.InvariantCulture), season.Label, startsOn, endsOn));
        }

        logger.LogInformation("futbolasturiano.es: {Count} ligas seleccionadas de {Total} para la temporada {Season}",
            result.Count, leagues.Count, season.Label);

        return result;
    }

    public async Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        var teams = (await GetLeagueTeamItemsAsync(competitionExternalId, cancellationToken))
            .Select(ToTeam).OfType<TeamSnapshot>().ToList();

        // Equipos que juegan partidos de la liga pero que la web no ha etiquetado en ella: se recuperan por id.
        var known = teams.Select(t => t.ExternalId).ToHashSet();
        var missing = (await GetEventsAsync(competitionExternalId, _window, cancellationToken)).Items
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
                var extra = (await GetPagedAsync($"wp/v2/sp_team?include={string.Join(',', chunk)}&{TeamFields}", cancellationToken)).Items;
                RegisterSlugs(extra);
                teams.AddRange(extra.Select(ToTeam).OfType<TeamSnapshot>());
            }
        }

        // Equipos que solo aparecen en partidos ocultos por la API (leídos de su página): se localizaron por su slug.
        if (_extraTeamsByLeague.TryGetValue(competitionExternalId, out var hiddenOnly))
        {
            known = teams.Select(t => t.ExternalId).ToHashSet();
            teams.AddRange(hiddenOnly.Where(t => known.Add(t.ExternalId)));
        }

        return teams;
    }

    public async Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(
        string competitionExternalId,
        IReadOnlyCollection<MatchSnapshot> known,
        MatchWindow? window,
        CancellationToken cancellationToken)
    {
        _window = window;
        var venues = await GetVenuesAsync(cancellationToken);
        var events = await GetEventsAsync(competitionExternalId, window, cancellationToken);

        var matches = new List<MatchSnapshot>(events.Items.Count);
        var visible = new List<VisibleEvent>(events.Items.Count);

        foreach (var item in events.Items)
        {
            var id = Json.GetInt(item, "id");
            var teams = Json.GetIntArray(item, "teams");
            var matchday = Json.GetInt(item, "day");
            var kickoff = KickoffOf(item);

            if (id is not null) visible.Add(new VisibleEvent(id.Value, kickoff ?? DateTimeOffset.MaxValue));

            // La API se pide con un día de margen: aquí se deja exactamente la ventana (en hora de Madrid).
            if (window is not null && (kickoff is null || !window.Contains(LocalDay(kickoff.Value)))) continue;

            if (id is null || teams.Count < 2 || matchday is null or < 1 || kickoff is null)
            {
                logger.LogDebug("Evento {Id} ignorado: datos incompletos (equipos={Teams}, jornada={Day})", id, teams.Count, matchday);
                continue;
            }

            var home = teams[0];
            var away = teams[1];
            var homeGoals = Json.GetGoals(item, home);
            var awayGoals = Json.GetGoals(item, away);
            var hasScore = homeGoals is not null && awayGoals is not null;

            // Con marcador pero sin desenlace (win/draw/loss) y recién empezado: es el marcador en directo.
            var live = hasScore && !Json.HasOutcome(item, home) && kickoff.Value > clock.GetUtcNow() - LiveWindow;
            var played = hasScore && !live;

            var venueIds = Json.GetIntArray(item, "sp_venue");
            var venue = venueIds.Count > 0 && venues.TryGetValue(venueIds[0], out var venueName) ? venueName : null;

            matches.Add(new MatchSnapshot(
                id.Value.ToString(CultureInfo.InvariantCulture),
                matchday.Value,
                home.ToString(CultureInfo.InvariantCulture),
                away.ToString(CultureInfo.InvariantCulture),
                kickoff.Value,
                live ? MatchStatus.InProgress : played ? MatchStatus.Played : MatchStatus.Scheduled,
                played ? homeGoals : null,
                played ? awayGoals : null,
                venue));
        }

        if (_settings.RecoverHiddenEvents)
        {
            matches.AddRange(window is null
                ? await RecoverHiddenEventsAsync(competitionExternalId, visible, events.Total, known, cancellationToken)
                : await RefreshHiddenInWindowAsync(competitionExternalId, visible, known, window, cancellationToken));
        }

        return matches;
    }

    public async Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        var season = await ResolveSeasonAsync(cancellationToken);
        var leagueId = int.Parse(competitionExternalId, CultureInfo.InvariantCulture);

        _tables ??= (await GetPagedAsync("sportspress/v2/sp_table?_fields=id,title,modified,sp_league,sp_season,data", cancellationToken)).Items;

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
    // Partidos ocultos por la API: se leen de su página pública
    // ---------------------------------------------------------------------------------------------

    private async Task<List<MatchSnapshot>> RecoverHiddenEventsAsync(
        string competitionExternalId,
        IReadOnlyCollection<VisibleEvent> visible,
        int total,
        IReadOnlyCollection<MatchSnapshot> known,
        CancellationToken cancellationToken)
    {
        var plan = HiddenEventPlanner.Plan(
            visible, total, known, clock.GetUtcNow(),
            TimeSpan.FromDays(Math.Max(0, _settings.HiddenEventRefreshDays)), RefreshBehind);

        if (plan.Hidden <= 0) return [];

        var league = await GetLeagueNameAsync(competitionExternalId, cancellationToken);
        var season = await ResolveSeasonAsync(cancellationToken);
        var knownById = known.GroupBy(k => k.ExternalId).ToDictionary(g => g.Key, g => g.First());

        var recovered = new List<MatchSnapshot>(plan.Reused);
        var budget = Math.Max(0, _settings.HiddenEventPagesPerLeague);
        var downloaded = 0;
        var refreshed = 0;
        var foreign = 0;
        var nextId = plan.NextId;
        var extensions = 0;

        var queue = new Queue<int>(plan.Refresh.Concat(plan.Unknown));

        while (queue.Count > 0 && recovered.Count < plan.Hidden)
        {
            var id = queue.Dequeue();
            var externalId = id.ToString(CultureInfo.InvariantCulture);
            knownById.TryGetValue(externalId, out var previous);

            if (!_eventPages.TryGetValue(id, out var page))
            {
                if (downloaded >= budget)
                {
                    // Sin presupuesto: lo ya conocido se mantiene tal cual; lo nuevo queda para la próxima sincronización.
                    if (previous is not null) recovered.Add(previous);
                    continue;
                }

                var fetch = await FetchEventPageAsync(id, cancellationToken);
                downloaded++;

                if (fetch.Failed)
                {
                    if (previous is not null) recovered.Add(previous);
                    continue;
                }

                page = fetch.Page;
                _eventPages[id] = page;
            }

            if (page is null || !page.IsMatch || !page.BelongsTo(league, season.Label))
            {
                // No es un partido de esta liga (otro contenido u otra competición): el bloque se alarga un id.
                foreign++;
                if (extensions < MaxBlockExtensions)
                {
                    queue.Enqueue(nextId++);
                    extensions++;
                }

                continue;
            }

            var snapshot = await ToSnapshotAsync(id, page, competitionExternalId, cancellationToken);
            if (snapshot is null)
            {
                if (previous is not null) recovered.Add(previous);
                continue;
            }

            if (previous is not null) refreshed++;
            recovered.Add(snapshot);
        }

        var pending = plan.Hidden - recovered.Count;
        logger.LogInformation(
            "futbolasturiano.es: liga {League} ({Name}): {Hidden} partidos ocultos por la API; {Recovered} recuperados " +
            "({Downloaded} páginas leídas, {Refreshed} actualizados, {Reused} reutilizados, {Foreign} ajenos); {Pending} pendientes para la próxima sincronización",
            competitionExternalId, league, plan.Hidden, recovered.Count, downloaded, refreshed, plan.Reused.Count, foreign, Math.Max(0, pending));

        return recovered;
    }

    // Con una ventana de fechas no se ve el bloque entero de la liga, así que no se buscan partidos ocultos nuevos (eso
    // lo hace la pasada completa): solo se releen los ya guardados de esos días que la API no ha devuelto y que ya han
    // empezado o están a punto, sin resultado todavía.
    private async Task<List<MatchSnapshot>> RefreshHiddenInWindowAsync(
        string competitionExternalId,
        IReadOnlyCollection<VisibleEvent> visible,
        IReadOnlyCollection<MatchSnapshot> known,
        MatchWindow window,
        CancellationToken cancellationToken)
    {
        var returned = visible.Select(v => v.Id).ToHashSet();
        var hidden = known
            .Select(k => (Id: int.TryParse(k.ExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0, Snapshot: k))
            .Where(k => k.Id > 0 && !returned.Contains(k.Id) && window.Contains(LocalDay(k.Snapshot.KickoffAt)))
            .ToList();
        var due = clock.GetUtcNow() + HiddenRoundLookAhead;
        var candidates = hidden
            .Where(k => k.Snapshot.Status != MatchStatus.Played && k.Snapshot.KickoffAt <= due)
            .OrderBy(k => k.Snapshot.KickoffAt)
            .ToList();

        if (candidates.Count == 0)
        {
            if (hidden.Count > 0)
                logger.LogInformation("futbolasturiano.es: liga {League}, {From}–{To}: {Hidden} partidos ocultos guardados en esas fechas; ninguno pendiente de resultado todavía",
                    competitionExternalId, window.From, window.To, hidden.Count);
            return [];
        }

        var league = await GetLeagueNameAsync(competitionExternalId, cancellationToken);
        var season = await ResolveSeasonAsync(cancellationToken);
        var budget = Math.Max(0, _settings.HiddenEventPagesPerLeague);
        var refreshed = new List<MatchSnapshot>();
        var downloaded = 0;

        foreach (var (id, _) in candidates)
        {
            if (downloaded >= budget) break;

            if (!_eventPages.TryGetValue(id, out var page))
            {
                var fetch = await FetchEventPageAsync(id, cancellationToken);
                downloaded++;
                if (fetch.Failed) continue;

                page = fetch.Page;
                _eventPages[id] = page;
            }

            if (page is null || !page.IsMatch || !page.BelongsTo(league, season.Label)) continue;
            if (await ToSnapshotAsync(id, page, competitionExternalId, cancellationToken) is { } snapshot) refreshed.Add(snapshot);
        }

        logger.LogInformation(
            "futbolasturiano.es: liga {League} ({Name}), {From}–{To}: {Hidden} partidos ocultos guardados en esas fechas, {Candidates} empezados o a punto sin resultado; {Refreshed} releídos ({Downloaded} páginas)",
            competitionExternalId, league, window.From, window.To, hidden.Count, candidates.Count, refreshed.Count, downloaded);

        return refreshed;
    }

    private DateOnly LocalDay(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, SiteTimeZone).DateTime);

    private async Task<MatchSnapshot?> ToSnapshotAsync(int id, EventPage page, string competitionExternalId, CancellationToken cancellationToken)
    {
        var home = await TeamIdBySlugAsync(page.HomeTeamSlug!, competitionExternalId, cancellationToken);
        var away = await TeamIdBySlugAsync(page.AwayTeamSlug!, competitionExternalId, cancellationToken);

        if (home is null || away is null || home == away)
        {
            logger.LogWarning("futbolasturiano.es: partido oculto {Id} ignorado: equipos no identificados ({Home} vs {Away})",
                id, page.HomeTeamSlug, page.AwayTeamSlug);
            return null;
        }

        return new MatchSnapshot(
            id.ToString(CultureInfo.InvariantCulture),
            page.Matchday!.Value,
            home.Value.ToString(CultureInfo.InvariantCulture),
            away.Value.ToString(CultureInfo.InvariantCulture),
            SiteClock.ToUtc(page.LocalKickoff!.Value, SiteTimeZone),
            page.Live ? MatchStatus.InProgress : page.Played ? MatchStatus.Played : MatchStatus.Scheduled,
            page.Played ? page.HomeGoals : null,
            page.Played ? page.AwayGoals : null,
            page.Venue);
    }

    public async Task<IReadOnlyList<MatchOfficialData>?> GetMatchOfficialsAsync(string matchExternalId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(matchExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return [];
        try
        {
            using var response = await SendAsync($"/?p={id}", "text/html", allowNotFound: true, cancellationToken);
            if (response is null) return [];
            return EventPageParser.ParseOfficials(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "futbolasturiano.es: no se pudo leer el equipo arbitral del partido {Id}", id);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("futbolasturiano.es: timeout leyendo el equipo arbitral del partido {Id}", id);
            return null;
        }
    }

    private async Task<(EventPage? Page, bool Failed)> FetchEventPageAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync($"/?p={id}", "text/html", allowNotFound: true, cancellationToken);
            if (response is null) return (null, false);

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return (EventPageParser.Parse(html), false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "futbolasturiano.es: no se pudo leer la página del partido {Id}", id);
            return (null, true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("futbolasturiano.es: timeout leyendo la página del partido {Id}", id);
            return (null, true);
        }
    }

    private async Task<int?> TeamIdBySlugAsync(string slug, string competitionExternalId, CancellationToken cancellationToken)
    {
        await GetLeagueTeamItemsAsync(competitionExternalId, cancellationToken);
        if (_teamIdBySlug.TryGetValue(slug, out var id)) return id;
        if (_unknownSlugs.Contains(slug)) return null;

        var found = (await GetPagedAsync($"wp/v2/sp_team?slug={Uri.EscapeDataString(slug)}&{TeamFields}", cancellationToken)).Items;
        RegisterSlugs(found);

        var extra = _extraTeamsByLeague.TryGetValue(competitionExternalId, out var list) ? list : _extraTeamsByLeague[competitionExternalId] = [];
        extra.AddRange(found.Select(ToTeam).OfType<TeamSnapshot>());

        if (_teamIdBySlug.TryGetValue(slug, out id)) return id;

        _unknownSlugs.Add(slug);
        return null;
    }

    private void RegisterSlugs(IEnumerable<JsonElement> teams)
    {
        foreach (var team in teams)
        {
            var id = Json.GetInt(team, "id");
            var slug = Json.GetString(team, "slug");
            if (id is > 0 && !string.IsNullOrWhiteSpace(slug))
                _teamIdBySlug[slug] = id.Value;
        }
    }

    private async Task<string> GetLeagueNameAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        if (_leagueNames.TryGetValue(competitionExternalId, out var name)) return name;

        using var response = await SendAsync($"wp/v2/sp_league/{competitionExternalId}?_fields=id,name", "application/json", allowNotFound: false, cancellationToken);
        await using var stream = await response!.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        name = WebUtility.HtmlDecode(Json.GetString(document.RootElement, "name") ?? string.Empty);
        _leagueNames[competitionExternalId] = name;
        return name;
    }

    // ---------------------------------------------------------------------------------------------
    // Catálogos cacheados por sincronización
    // ---------------------------------------------------------------------------------------------

    private DateTimeOffset? KickoffOf(JsonElement item)
    {
        // "date" es la fecha real (hora peninsular); "date_gmt" se queda a veces con la fecha provisional del calendario.
        var local = Json.GetLocalDateTime(item, "date");
        return local is not null ? SiteClock.ToUtc(local.Value, SiteTimeZone) : Json.GetUtcDate(item, "date_gmt");
    }

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

    private async Task<List<JsonElement>> GetLeagueTeamItemsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        if (_teamsByLeague.TryGetValue(competitionExternalId, out var cached)) return cached;

        var season = await ResolveSeasonAsync(cancellationToken);
        var items = (await GetPagedAsync(
            $"wp/v2/sp_team?sp_league={competitionExternalId}&sp_season={season.Id}&{TeamFields}",
            cancellationToken)).Items;

        RegisterSlugs(items);
        _teamsByLeague[competitionExternalId] = items;
        return items;
    }

    // Una sincronización trabaja con una sola ventana (o ninguna): la primera lectura de cada liga se reutiliza.
    private async Task<Paged> GetEventsAsync(string competitionExternalId, MatchWindow? window, CancellationToken cancellationToken)
    {
        if (_eventsByLeague.TryGetValue(competitionExternalId, out var cached)) return cached;

        var season = await ResolveSeasonAsync(cancellationToken);

        // after/before filtran por "date" (hora de Madrid); se pide un día de margen a cada lado y se recorta después.
        var dates = window is null
            ? string.Empty
            : $"&after={window.From.AddDays(-1):yyyy-MM-dd}T00:00:00&before={window.To.AddDays(2):yyyy-MM-dd}T00:00:00";

        // Orden por id (único y estable): con orderby=date los partidos que comparten fecha cambian de página entre
        // peticiones y la paginación devuelve algunos repetidos y se salta otros.
        var events = await GetPagedAsync(
            $"wp/v2/sp_event?sp_league={competitionExternalId}&sp_season={season.Id}{dates}&orderby=id&order=asc&_fields=id,date,date_gmt,day,teams,results,sp_venue",
            cancellationToken);

        var distinct = events.Items
            .GroupBy(e => Json.GetInt(e, "id") ?? 0)
            .Select(g => g.First())
            .ToList();

        if (distinct.Count < events.Items.Count)
            logger.LogWarning("futbolasturiano.es: la API devolvió {Duplicates} partidos repetidos en la liga {League}; se descartan",
                events.Items.Count - distinct.Count, competitionExternalId);

        var result = events with { Items = distinct };
        _eventsByLeague[competitionExternalId] = result;
        return result;
    }

    private async Task<(int Id, string Label)> ResolveSeasonAsync(CancellationToken cancellationToken)
    {
        if (_season is { } cached) return cached;

        var seasons = (await GetPagedAsync("wp/v2/sp_season?hide_empty=false&_fields=id,name", cancellationToken)).Items;
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

        var venues = (await GetPagedAsync("wp/v2/sp_venue?hide_empty=false&_fields=id,name", cancellationToken)).Items;
        _venues = venues
            .Select(v => (Id: Json.GetInt(v, "id") ?? 0, Name: WebUtility.HtmlDecode(Json.GetString(v, "name") ?? string.Empty)))
            .Where(v => v.Id > 0 && v.Name.Length > 0)
            .ToDictionary(v => v.Id, v => v.Name);

        return _venues;
    }

    private async Task<Dictionary<int, Position?>> GetPositionsAsync(CancellationToken cancellationToken)
    {
        if (_positions is not null) return _positions;

        var positions = (await GetPagedAsync("wp/v2/sp_position?hide_empty=false&_fields=id,name", cancellationToken)).Items;
        _positions = positions
            .Select(p => (Id: Json.GetInt(p, "id") ?? 0, Name: Json.GetString(p, "name") ?? string.Empty))
            .Where(p => p.Id > 0)
            .ToDictionary(p => p.Id, p => MapPosition(p.Name));

        return _positions;
    }

    private async Task<ILookup<int, JsonElement>> GetPlayersByTeamAsync(CancellationToken cancellationToken)
    {
        if (_playersByTeam is not null) return _playersByTeam;

        var players = (await GetPagedAsync("wp/v2/sp_player?_fields=id,title,number,sp_position,current_teams", cancellationToken)).Items;

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
    // HTTP: paginación de la API REST de WordPress (cabeceras X-WP-Total / X-WP-TotalPages), ritmo y reintentos
    // ---------------------------------------------------------------------------------------------

    private sealed record Paged(List<JsonElement> Items, int Total);

    private async Task<Paged> GetPagedAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        var separator = pathAndQuery.Contains('?') ? '&' : '?';
        var page = 1;
        var total = 0;
        int totalPages;

        do
        {
            using var response = await SendAsync($"{pathAndQuery}{separator}per_page={_settings.PageSize}&page={page}", "application/json", allowNotFound: false, cancellationToken);

            totalPages = HeaderInt(response!, "X-WP-TotalPages") ?? 1;
            if (page == 1) total = HeaderInt(response!, "X-WP-Total") ?? 0;

            await using var stream = await response!.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
                items.AddRange(document.RootElement.EnumerateArray().Select(e => e.Clone()));

            page++;
        }
        while (page <= totalPages);

        return new Paged(items, Math.Max(total, items.Count));
    }

    private static int? HeaderInt(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var parsed) ? parsed : null;

    private async Task<HttpResponseMessage?> SendAsync(string pathAndQuery, string accept, bool allowNotFound, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await ThrottleAsync(cancellationToken);

            HttpResponseMessage? response = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return response;

                var status = (int)response.StatusCode;
                response.Dispose();

                if (allowNotFound && status == 404)
                    return null;

                if (attempt >= MaxRetries || status is >= 400 and < 500 and not 429)
                    throw new HttpRequestException($"futbolasturiano.es respondió {status} a {pathAndQuery}");

                logger.LogWarning("futbolasturiano.es respondió {Status} a {Path}; reintento {Attempt}/{Max}", status, pathAndQuery, attempt, MaxRetries);
            }
            catch (HttpRequestException) when (attempt < MaxRetries && response is null)
            {
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
