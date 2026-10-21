using System.Globalization;
using System.Text;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Infrastructure.FootballData.Fake;

/// <summary>
/// Proveedor de desarrollo sin red: genera tres ligas ficticias (3ª RFEF, 1ª AsturFútbol y 2ª AsturFútbol · Grupo 1)
/// de 12 equipos, con calendario completo a doble vuelta, resultados hasta la fecha actual, clasificación y
/// plantillas (solo en las dos primeras, como ocurre en futbolasturiano.es). Es determinista: los identificadores
/// y los resultados no cambian entre arranques, así que la sincronización es idempotente y la liga "avanza" sola.
/// </summary>
internal sealed class FakeFootballDataProvider(TimeProvider clock) : IFootballDataProvider
{
    private const string SeasonId = "2026-27";
    private const string SeasonLabel = "2026/2027";
    private const int MatchdaysPerHalf = 11;
    private const int PlayersPerTeam = 18;

    private static readonly DateOnly SeasonStart = new(2026, 9, 6);
    private static readonly DateOnly SeasonEnd = new(2027, 5, 30);

    private sealed record League(string Id, string Name, string Category, CompetitionLevel Level, int Tier, string? Group, bool PublishesRosters, int FirstTeam);

    private static readonly League[] Leagues =
    [
        new("3-rfef", "3ª RFEF", "3ª RFEF", CompetitionLevel.National, 3, null, true, 0),
        new("1-asturfutbol", "1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, true, 12),
        new("2-asturfutbol-g1", "2ª AsturFútbol · Grupo 1", "2ª AsturFútbol", CompetitionLevel.Regional, 2, "1", false, 24),
    ];

    private static readonly (string Id, string Name, string ShortName, int Strength)[] TeamSeeds =
    [
        ("nalon", "CD Nalón", "NAL", 78), ("cabranes", "UD Cabranes", "CAB", 73), ("candamo", "Sporting de Candamo", "CAN", 70),
        ("pravia", "Racing de Pravia", "PRA", 68), ("cudillero", "CD Cudillero", "CUD", 66), ("norena", "Atlético Noreña", "NOR", 64),
        ("siero", "UD Siero", "SIE", 62), ("quiros", "CD Quirós", "QUI", 58), ("colombres", "SD Colombres", "COL", 56),
        ("valdes", "Unión Valdés", "VAL", 54), ("bimenes", "CD Bimenes", "BIM", 50), ("tapia", "Atlético Tapia", "TAP", 46),

        ("llanera", "Atlético Llanera", "LLA", 76), ("grado", "CD Grado", "GRA", 72), ("tineo", "UD Tineo", "TIN", 69),
        ("cangas", "SD Cangas", "CGS", 67), ("ribadesella", "CD Ribadesella", "RIB", 65), ("salas", "CD Salas", "SAL", 63),
        ("castropol", "UD Castropol", "CAS", 60), ("piloña", "CD Piloña", "PIL", 57), ("nava", "CD Nava", "NAV", 55),
        ("laviana", "CD Laviana", "LAV", 53), ("aller", "Atlético Aller", "ALL", 49), ("boal", "CD Boal", "BOA", 45),

        ("belmonte", "CD Belmonte", "BEL", 74), ("carreño", "Atlético Carreño", "CAR", 71), ("gozon", "UD Gozón", "GOZ", 68),
        ("illas", "CD Illas", "ILL", 66), ("lena", "SD Lena", "LEN", 64), ("morcin", "CD Morcín", "MOR", 62),
        ("proaza", "CD Proaza", "PRO", 59), ("ribera", "Ribera de Arriba CF", "RIA", 57), ("sariego", "CD Sariego", "SAR", 54),
        ("somiedo", "UD Somiedo", "SOM", 52), ("teverga", "CD Teverga", "TEV", 48), ("yernes", "Atlético Yernes", "YER", 44),
    ];

    private static readonly string[] FirstNames =
    [
        "Pelayo", "Xuan", "Nel", "Adrián", "Pablo", "Hugo", "Mario", "Diego", "Álvaro", "Iván",
        "Sergio", "Daniel", "Javier", "Rubén", "Marcos", "Nicolás", "Samuel", "Aitor", "Borja", "Jorge",
        "Lucas", "Mateo", "Enol", "Covadonga", "Andrés", "Guillermo", "Raúl", "Víctor", "Miguel", "Carlos",
    ];

    private static readonly string[] LastNames =
    [
        "Fernández", "Álvarez", "García", "Menéndez", "Suárez", "Rodríguez", "Martínez", "López", "González", "Díaz",
        "Cuervo", "Llaneza", "Iglesias", "Noval", "Viejo", "Riestra", "Cabal", "Busto", "Prendes", "Coto",
        "Solís", "Lastra", "Ordóñez", "Pola", "Canga", "Valdés", "Tuñón", "Braña", "Fueyo", "Vigil",
    ];

    private static readonly TimeZoneInfo MadridTimeZone = ResolveMadridTimeZone();

    public string ProviderName => FootballDataOptions.FakeProvider;

    public Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CompetitionSnapshot>>(Leagues
            .Select(l => new CompetitionSnapshot(l.Id, l.Name, l.Category, l.Level, l.Tier, l.Group, "Asturias", SeasonId, SeasonLabel, SeasonStart, SeasonEnd))
            .ToList());

    public Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TeamSnapshot>>(TeamsOf(competitionExternalId)
            .Select(t => new TeamSnapshot(t.Id, t.Name, t.ShortName, null))
            .ToList());

    public Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(string competitionExternalId, IReadOnlyCollection<MatchSnapshot> known, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MatchSnapshot>>(BuildMatches(competitionExternalId, clock.GetUtcNow()));

    public Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken)
    {
        var played = BuildMatches(competitionExternalId, clock.GetUtcNow()).Where(m => m.Status == MatchStatus.Played).ToList();

        var rows = TeamsOf(competitionExternalId).Select(team =>
        {
            var home = played.Where(m => m.HomeTeamExternalId == team.Id).ToList();
            var away = played.Where(m => m.AwayTeamExternalId == team.Id).ToList();

            var won = home.Count(m => m.HomeGoals > m.AwayGoals) + away.Count(m => m.AwayGoals > m.HomeGoals);
            var drawn = home.Count(m => m.HomeGoals == m.AwayGoals) + away.Count(m => m.AwayGoals == m.HomeGoals);
            var lost = home.Count + away.Count - won - drawn;
            var goalsFor = home.Sum(m => m.HomeGoals ?? 0) + away.Sum(m => m.AwayGoals ?? 0);
            var goalsAgainst = home.Sum(m => m.AwayGoals ?? 0) + away.Sum(m => m.HomeGoals ?? 0);

            return (team.Id, Played: home.Count + away.Count, won, drawn, lost, goalsFor, goalsAgainst, Points: won * 3 + drawn);
        })
        .OrderByDescending(r => r.Points)
        .ThenByDescending(r => r.goalsFor - r.goalsAgainst)
        .ThenByDescending(r => r.goalsFor)
        .ThenBy(r => r.Id)
        .Select((r, index) => new StandingSnapshot(r.Id, index + 1, r.Played, r.won, r.drawn, r.lost, r.goalsFor, r.goalsAgainst, r.Points))
        .ToList();

        return Task.FromResult<IReadOnlyList<StandingSnapshot>>(rows);
    }

    public Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken)
    {
        var league = Leagues.FirstOrDefault(l => TeamsOf(l.Id).Any(t => t.Id == teamExternalId));
        if (league is null || !league.PublishesRosters)
            return Task.FromResult<IReadOnlyList<RosterEntrySnapshot>>([]);

        var random = StableRandom($"roster:{teamExternalId}");
        var usedNames = new HashSet<string>();
        var roster = new List<RosterEntrySnapshot>(PlayersPerTeam);

        for (var number = 1; number <= PlayersPerTeam; number++)
        {
            string fullName;
            do
            {
                fullName = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]} {LastNames[random.Next(LastNames.Length)]}";
            }
            while (!usedNames.Add(fullName));

            var position = number switch
            {
                <= 2 => Position.Goalkeeper,
                <= 8 => Position.Defender,
                <= 14 => Position.Midfielder,
                _ => Position.Forward,
            };

            roster.Add(new RosterEntrySnapshot($"{teamExternalId}-{number}", fullName, position, number));
        }

        return Task.FromResult<IReadOnlyList<RosterEntrySnapshot>>(roster);
    }

    private static IEnumerable<(string Id, string Name, string ShortName, int Strength)> TeamsOf(string leagueId)
    {
        var league = Leagues.FirstOrDefault(l => l.Id == leagueId);
        return league is null ? [] : TeamSeeds.Skip(league.FirstTeam).Take(12);
    }

    /// <summary>Calendario a doble vuelta por el método del círculo; resultados para las jornadas ya pasadas.</summary>
    private static List<MatchSnapshot> BuildMatches(string leagueId, DateTimeOffset now)
    {
        var seeds = TeamsOf(leagueId).ToList();
        if (seeds.Count == 0) return [];

        var teams = seeds.Select(t => t.Id).ToList();
        var strength = seeds.ToDictionary(t => t.Id, t => t.Strength);
        var names = seeds.ToDictionary(t => t.Id, t => t.Name);
        var matches = new List<MatchSnapshot>();

        for (var round = 0; round < MatchdaysPerHalf; round++)
        {
            var pairs = new List<(string Home, string Away)>();
            var count = teams.Count;

            for (var i = 0; i < count / 2; i++)
            {
                var a = teams[i];
                var b = teams[count - 1 - i];
                pairs.Add(round % 2 == 0 ? (a, b) : (b, a));
            }

            for (var half = 0; half < 2; half++)
            {
                var matchday = round + 1 + half * MatchdaysPerHalf;
                var kickoff = KickoffFor(matchday);

                foreach (var (home, away) in pairs)
                {
                    var (h, a) = half == 0 ? (home, away) : (away, home);
                    var externalId = $"{leagueId}-{SeasonId}-j{matchday:00}-{h}-{a}";
                    var played = kickoff.AddHours(2) < now;

                    int? homeGoals = null, awayGoals = null;
                    if (played)
                        (homeGoals, awayGoals) = SimulateScore(externalId, strength[h], strength[a]);

                    matches.Add(new MatchSnapshot(
                        externalId, matchday, h, a, kickoff,
                        played ? MatchStatus.Played : MatchStatus.Scheduled,
                        homeGoals, awayGoals,
                        $"Campo municipal de {names[h].Split(' ').Last()}"));
                }
            }

            var last = teams[^1];
            teams.RemoveAt(teams.Count - 1);
            teams.Insert(1, last);
        }

        return matches.OrderBy(m => m.Matchday).ThenBy(m => m.ExternalId, StringComparer.Ordinal).ToList();
    }

    private static DateTimeOffset KickoffFor(int matchday)
    {
        var date = SeasonStart.AddDays(7 * (matchday - 1));
        var local = date.ToDateTime(new TimeOnly(17, 0));
        return new DateTimeOffset(local, MadridTimeZone.GetUtcOffset(local));
    }

    private static (int Home, int Away) SimulateScore(string matchId, int homeStrength, int awayStrength)
    {
        var random = StableRandom($"score:{matchId}");
        var homeExpected = Math.Max(0.2, 1.45 + (homeStrength - awayStrength) / 35.0);
        var awayExpected = Math.Max(0.2, 1.05 + (awayStrength - homeStrength) / 35.0);
        return (Poisson(random, homeExpected), Poisson(random, awayExpected));
    }

    private static int Poisson(Random random, double lambda)
    {
        var limit = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;

        do
        {
            k++;
            p *= random.NextDouble();
        }
        while (p > limit);

        return k - 1;
    }

    /// <summary>Random reproducible a partir de una clave (FNV-1a), para que los datos no cambien entre arranques.</summary>
    private static Random StableRandom(string key)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var b in Encoding.UTF8.GetBytes(key))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return new Random((int)hash);
        }
    }

    private static TimeZoneInfo ResolveMadridTimeZone()
    {
        foreach (var id in new[] { "Europe/Madrid", "Romance Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Madrid-Fallback", TimeSpan.FromHours(1), "Madrid", "Madrid");
    }
}
