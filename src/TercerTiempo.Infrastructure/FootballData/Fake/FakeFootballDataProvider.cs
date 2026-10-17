using System.Text;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Infrastructure.FootballData.Fake;

/// <summary>
/// Proveedor de desarrollo: genera una liga regional ficticia de 12 equipos (calendario completo a doble vuelta,
/// resultados hasta la fecha actual, clasificación y plantillas). Es determinista: los identificadores y los
/// resultados no cambian entre arranques, así que la sincronización es idempotente y la liga "avanza" sola
/// a medida que pasan las jornadas.
/// </summary>
internal sealed class FakeFootballDataProvider(TimeProvider clock) : IFootballDataProvider
{
    private const string CompetitionId = "ast-preferente";
    private const string SeasonId = "2026-27";
    private const int MatchdaysPerHalf = 11;
    private const int PlayersPerTeam = 18;

    private static readonly DateOnly SeasonStart = new(2026, 9, 6);
    private static readonly DateOnly SeasonEnd = new(2027, 5, 30);

    private static readonly (string Id, string Name, string ShortName, int Strength)[] TeamSeeds =
    [
        ("nalon", "CD Nalón", "NAL", 78),
        ("cabranes", "UD Cabranes", "CAB", 73),
        ("candamo", "Sporting de Candamo", "CAN", 70),
        ("pravia", "Racing de Pravia", "PRA", 68),
        ("cudillero", "CD Cudillero", "CUD", 66),
        ("norena", "Atlético Noreña", "NOR", 64),
        ("siero", "UD Siero", "SIE", 62),
        ("quiros", "CD Quirós", "QUI", 58),
        ("colombres", "SD Colombres", "COL", 56),
        ("valdes", "Unión Valdés", "VAL", 54),
        ("bimenes", "CD Bimenes", "BIM", 50),
        ("tapia", "Atlético Tapia", "TAP", 46),
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

    public Task<CompetitionSeasonSnapshot> GetCompetitionSeasonAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CompetitionSeasonSnapshot(
            CompetitionId, "Regional Preferente de Asturias", "Asturias",
            SeasonId, "2026/27", SeasonStart, SeasonEnd));

    public Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TeamSnapshot>>(
            TeamSeeds.Select(t => new TeamSnapshot(t.Id, t.Name, t.ShortName, null)).ToList());

    public Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MatchSnapshot>>(BuildMatches(clock.GetUtcNow()));

    public Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken)
    {
        var played = BuildMatches(clock.GetUtcNow()).Where(m => m.Status == MatchStatus.Played).ToList();

        var rows = TeamSeeds.Select(team =>
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
        if (TeamSeeds.All(t => t.Id != teamExternalId))
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

    /// <summary>Calendario a doble vuelta por el método del círculo; resultados para las jornadas ya pasadas.</summary>
    private static List<MatchSnapshot> BuildMatches(DateTimeOffset now)
    {
        var teams = TeamSeeds.Select(t => t.Id).ToList();
        var strength = TeamSeeds.ToDictionary(t => t.Id, t => t.Strength);
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

            // Primera y segunda vuelta (local/visitante invertidos)
            for (var half = 0; half < 2; half++)
            {
                var matchday = round + 1 + half * MatchdaysPerHalf;
                var kickoff = KickoffFor(matchday);

                foreach (var (home, away) in pairs)
                {
                    var (h, a) = half == 0 ? (home, away) : (away, home);
                    var externalId = $"{SeasonId}-j{matchday:00}-{h}-{a}";
                    var played = kickoff.AddHours(2) < now;

                    int? homeGoals = null, awayGoals = null;
                    if (played)
                        (homeGoals, awayGoals) = SimulateScore(externalId, strength[h], strength[a]);

                    matches.Add(new MatchSnapshot(
                        externalId, matchday, h, a, kickoff,
                        played ? MatchStatus.Played : MatchStatus.Scheduled,
                        homeGoals, awayGoals,
                        $"Campo municipal de {TeamSeeds.First(t => t.Id == h).Name.Split(' ').Last()}"));
                }
            }

            // Rotación del círculo (el primer equipo queda fijo)
            var last = teams[^1];
            teams.RemoveAt(teams.Count - 1);
            teams.Insert(1, last);
        }

        return matches.OrderBy(m => m.Matchday).ThenBy(m => m.ExternalId).ToList();
    }

    private static DateTimeOffset KickoffFor(int matchday)
    {
        var date = SeasonStart.AddDays(7 * (matchday - 1));
        var local = date.ToDateTime(new TimeOnly(17, 0));
        var offset = MadridTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
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
