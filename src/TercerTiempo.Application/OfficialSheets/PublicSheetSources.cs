using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.OfficialSheets;

public sealed class PublicSheetOptions
{
    public const string SectionName = "OfficialSheets";

    public static readonly string[] DefaultSources = ["besoccer", "asturfutbol"];

    // Orden en que se consultan las webs del acta pública: la siguiente solo si la anterior no la tiene completa.
    // La federación bloquea la IP con pocas peticiones, así que va la última. Vacío = DefaultSources (el enlazado de
    // configuración añade a un array con valores en lugar de sustituirlos).
    public string[] Sources { get; set; } = [];

    public IReadOnlyList<string> EnabledSources =>
        (Sources is { Length: > 0 } ? Sources : DefaultSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Las actas guardadas de una web que se quita de Sources dejan de mostrarse (p. ej. BeSoccer en la beta cerrada).
    public bool Allows(string provider) => EnabledSources.Contains(provider, StringComparer.OrdinalIgnoreCase);
}

internal sealed record SheetFetch(string Provider, string ExternalId, string? SourceUrl, OfficialSheetData? Data)
{
    public bool Complete => Data is { Lines.Count: > 0 };
}

// Una web de la que se puede sacar el acta pública de un partido. null si no tiene ese partido.
internal interface IPublicSheetSource
{
    string Name { get; }

    // La federación solo se consulta para los partidos que importan (equipos con plantilla o que alguien abre).
    bool IsFederation { get; }

    Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken);
}

internal sealed class FederationSheetSource(
    IOfficialSheetProvider provider,
    OfficialSheetLocator locator,
    OfficialSheetSyncMemory memory,
    TimeProvider clock) : IPublicSheetSource
{
    public string Name => provider.ProviderName;

    public bool IsFederation => true;

    public async Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken)
    {
        var code = current?.Provider == Name ? current.ExternalId : null;
        if (code is null)
        {
            var now = clock.GetUtcNow();
            if (memory.SkipSeason(Name, season.Id, now)) return null;
            if (await locator.ResolveCompetitionAsync(competition, season, cancellationToken) is null)
            {
                memory.SeasonNotFound(Name, season.Id, now);
                return null;
            }

            code = (await locator.LocateAsync(match, competition, season, cancellationToken))?.ExternalId;
            if (code is null) return null;
        }

        return new SheetFetch(Name, code, provider.SheetUrl(code), await provider.GetSheetAsync(code, cancellationToken));
    }
}

internal sealed class DirectorySheetSource(
    IMatchSheetDirectory directory,
    ITeamStore teams,
    DirectoryTeamIndex index,
    OfficialSheetSyncMemory memory,
    TimeProvider clock,
    ILogger<DirectorySheetSource> logger) : IPublicSheetSource
{
    // Un partido aplazado se juega días después de la fecha del calendario; más allá ya sería otra jornada.
    private static readonly TimeSpan MaxDateGap = TimeSpan.FromDays(10);

    public string Name => directory.ProviderName;

    public bool IsFederation => false;

    public async Task<SheetFetch?> FetchAsync(Match match, Competition competition, Season season, OfficialMatchSheet? current, CancellationToken cancellationToken)
    {
        var code = current?.Provider == Name ? current.ExternalId : await LocateAsync(match, competition, season, cancellationToken);
        if (code is null) return null;

        return new SheetFetch(Name, code, directory.SheetUrl(code), await directory.GetSheetAsync(code, cancellationToken));
    }

    private async Task<string?> LocateAsync(Match match, Competition competition, Season season, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (memory.SkipSeason(Name, season.Id, now)) return null;

        var map = await index.GetAsync(Name, season.Id, now, () => BuildMapAsync(competition, season, cancellationToken));
        if (map.Count == 0)
        {
            memory.SeasonNotFound(Name, season.Id, now);
            return null;
        }

        var homeCode = map.GetValueOrDefault(match.HomeTeamId);
        var awayCode = map.GetValueOrDefault(match.AwayTeamId);
        var ours = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);

        foreach (var teamCode in new[] { homeCode, awayCode }.OfType<string>())
        {
            var fixtures = await directory.GetFixturesAsync(teamCode, cancellationToken);
            var found = fixtures
                .Where(f => f.StartsAt is { } at && (at - match.KickoffAt).Duration() <= MaxDateGap)
                .Select(f => (Fixture: f, Score: PairScore(f, homeCode, awayCode, ours.GetValueOrDefault(match.HomeTeamId), ours.GetValueOrDefault(match.AwayTeamId))))
                .Where(c => c.Score >= 1.5)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => (c.Fixture.StartsAt!.Value - match.KickoffAt).Duration())
                .Select(c => c.Fixture)
                .FirstOrDefault();

            if (found is not null) return found.Code;
        }

        logger.LogInformation("{Provider}: no se ha encontrado el partido {MatchId} (jornada {Matchday} de {Competition})", Name, match.Id, match.Matchday, competition.Name);
        return null;
    }

    // Cada lado vale 1 si coincide el código del equipo en la web y, si no se conoce, lo que se parezcan los nombres.
    private static double PairScore(DirectoryFixture fixture, string? homeCode, string? awayCode, Team? home, Team? away)
    {
        static double Side(string? code, Team? team, string theirCode, string theirName) =>
            code is not null ? (code == theirCode ? 1 : 0)
            : team is null ? 0
            : TeamNameSimilarity.Score(team.Name, theirName);

        return Side(homeCode, home, fixture.HomeCode, fixture.HomeName) + Side(awayCode, away, fixture.AwayCode, fixture.AwayName);
    }

    // Empareja nuestros equipos de la temporada con los del grupo de la web que más tenga en común. Se prueba primero el
    // grupo con nuestro número; su numeración no tiene por qué coincidir con la de la federación.
    private async Task<IReadOnlyDictionary<Guid, string>> BuildMapAsync(Competition competition, Season season, CancellationToken cancellationToken)
    {
        var groups = await directory.GetGroupsAsync(competition.Category, season.Label, cancellationToken);
        if (groups.Count == 0)
        {
            logger.LogInformation("{Provider}: no cubre la competición {Competition}", Name, competition.Name);
            return new Dictionary<Guid, string>();
        }

        var ours = await teams.GetBySeasonAsync(season.Id, cancellationToken);
        var ordered = groups.OrderBy(g => GroupKey(g) == GroupKey(competition.Group) ? 0 : 1).ToList();
        var best = new Dictionary<Guid, string>();

        foreach (var group in ordered)
        {
            var theirs = await directory.GetTeamsAsync(competition.Category, season.Label, group, cancellationToken);
            var map = Pair(ours, theirs);
            if (map.Count > best.Count) best = map;
            if (map.Count >= Math.Max(3, ours.Count / 2)) break;
        }

        logger.LogInformation("{Provider}: {Matched} de {Total} equipos de {Competition} localizados", Name, best.Count, ours.Count, competition.Name);
        return best;
    }

    internal static Dictionary<Guid, string> Pair(IReadOnlyList<Team> ours, IReadOnlyList<DirectoryTeam> theirs)
    {
        var candidates = new List<(double Score, Team Ours, DirectoryTeam Theirs)>();
        foreach (var team in ours)
        {
            foreach (var their in theirs)
            {
                var score = TeamNameSimilarity.Score(team.Name, their.Name);
                if (score >= 0.6) candidates.Add((score, team, their));
            }
        }

        var result = new Dictionary<Guid, string>();
        var used = new HashSet<string>();
        foreach (var (_, team, their) in candidates.OrderByDescending(c => c.Score))
        {
            if (result.ContainsKey(team.Id) || used.Contains(their.Code)) continue;
            result[team.Id] = their.Code;
            used.Add(their.Code);
        }
        return result;
    }

    private static string GroupKey(string? group) =>
        new(NameNormalizer.Normalize(group).Where(char.IsDigit).ToArray());
}

// Equipos de cada temporada ya emparejados con los de una web, para no recalcularlo en cada partido.
internal sealed class DirectoryTeamIndex
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<(string, Guid), (DateTimeOffset At, IReadOnlyDictionary<Guid, string> Map)> _maps = new();

    public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(string provider, Guid seasonId, DateTimeOffset now, Func<Task<IReadOnlyDictionary<Guid, string>>> build)
    {
        if (_maps.TryGetValue((provider, seasonId), out var cached) && now - cached.At < Lifetime)
            return cached.Map;

        var map = await build();
        _maps[(provider, seasonId)] = (now, map);
        return map;
    }
}
