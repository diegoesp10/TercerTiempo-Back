using System.Collections.Concurrent;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Stats.Predictions;

// Un partido de la temporada para el previsor: con resultado si ya contaba en la jornada elegida; sin él, se simula.
internal sealed record ForecastMatch(Guid HomeTeamId, Guid AwayTeamId, int? HomeGoals, int? AwayGoals);

// Puestos que cuentan en la clasificación final: hasta PromotionUntil, ascenso o promoción; desde RelegationFrom, descenso
// (null si es la última categoría).
internal sealed record ForecastZones(int PromotionUntil, int? RelegationFrom);

internal sealed record TeamForecast(
    Guid TeamId,
    int Position,
    int Points,
    int Played,
    int GoalsFor,
    int GoalsAgainst,
    double Champion,
    double Promotion,
    double Relegation,
    double ExpectedPoints,
    double ExpectedPosition,
    double[] PositionOdds,
    double Attack,
    double Defense);

// Previsor de la clasificación final con lo que se sabe en una jornada. Cada equipo tiene una fuerza de ataque y otra de
// defensa sacadas de sus goles (a favor y en contra, teniendo en cuenta contra quién y dónde jugó), que al principio de la
// liga pesan poco frente a la media; con ellas, los goles de cada partido pendiente siguen una Poisson (con la ventaja de
// jugar en casa) y se juega el resto de la temporada miles de veces. En cada simulación la fuerza de cada equipo varía un
// poco (rachas, lesiones, fichajes): sin eso, el modelo daría por hecho a mitad de liga lo que todavía puede cambiar. La
// clasificación de cada simulación desempata por diferencia de goles, goles a favor y sorteo.
internal static class LeagueForecaster
{
    private const double HomeGoalsPrior = 1.45;
    private const double AwayGoalsPrior = 1.15;
    private const double LeaguePriorMatches = 10;
    // Goles «de la media» que se suman a los de cada equipo: con pocos partidos su fuerza se queda cerca de 1.
    private const double TeamPriorGoals = 18;
    // Desviación (en escala logarítmica) de la fuerza de cada equipo de una simulación a otra.
    private const double SeasonUncertainty = 0.15;
    private const int RatingIterations = 25;

    public static IReadOnlyList<TeamForecast> Run(
        IReadOnlyList<Guid> teams, IReadOnlyList<ForecastMatch> matches, ForecastZones zones, int simulations, int seed)
    {
        var n = teams.Count;
        var index = teams.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var relevant = matches.Where(m => index.ContainsKey(m.HomeTeamId) && index.ContainsKey(m.AwayTeamId)).ToList();
        var played = relevant.Where(m => m.HomeGoals is not null && m.AwayGoals is not null)
            .Select(m => (H: index[m.HomeTeamId], A: index[m.AwayTeamId], HG: m.HomeGoals!.Value, AG: m.AwayGoals!.Value))
            .ToList();
        var pending = relevant.Where(m => m.HomeGoals is null || m.AwayGoals is null)
            .Select(m => (H: index[m.HomeTeamId], A: index[m.AwayTeamId]))
            .ToArray();

        var points = new int[n];
        var goalsFor = new int[n];
        var goalsAgainst = new int[n];
        var games = new int[n];
        foreach (var (h, a, hg, ag) in played)
        {
            games[h]++;
            games[a]++;
            goalsFor[h] += hg;
            goalsAgainst[h] += ag;
            goalsFor[a] += ag;
            goalsAgainst[a] += hg;
            points[h] += hg > ag ? 3 : hg == ag ? 1 : 0;
            points[a] += ag > hg ? 3 : hg == ag ? 1 : 0;
        }

        var (muHome, muAway, attack, defense) = Ratings(n, played);
        var simAttack = new double[n];
        var simDefense = new double[n];

        var random = new Random(seed);
        var finish = new int[n, n];
        var pointsSum = new long[n];
        var simPoints = new int[n];
        var simDiff = new int[n];
        var simFor = new int[n];
        var draw = new double[n];
        var order = new int[n];
        Comparison<int> ranking = (x, y) =>
            simPoints[y] != simPoints[x] ? simPoints[y].CompareTo(simPoints[x])
            : simDiff[y] != simDiff[x] ? simDiff[y].CompareTo(simDiff[x])
            : simFor[y] != simFor[x] ? simFor[y].CompareTo(simFor[x])
            : draw[x].CompareTo(draw[y]);

        for (var s = 0; s < simulations; s++)
        {
            for (var i = 0; i < n; i++)
            {
                simPoints[i] = points[i];
                simDiff[i] = goalsFor[i] - goalsAgainst[i];
                simFor[i] = goalsFor[i];
                draw[i] = random.NextDouble();
                order[i] = i;
                simAttack[i] = attack[i] * Math.Exp(SeasonUncertainty * Normal(random) - SeasonUncertainty * SeasonUncertainty / 2);
                simDefense[i] = defense[i] * Math.Exp(SeasonUncertainty * Normal(random) - SeasonUncertainty * SeasonUncertainty / 2);
            }
            for (var f = 0; f < pending.Length; f++)
            {
                var (h, a) = pending[f];
                var hg = Poisson(random, muHome * simAttack[h] * simDefense[a]);
                var ag = Poisson(random, muAway * simAttack[a] * simDefense[h]);
                simDiff[h] += hg - ag;
                simDiff[a] += ag - hg;
                simFor[h] += hg;
                simFor[a] += ag;
                if (hg > ag) simPoints[h] += 3;
                else if (ag > hg) simPoints[a] += 3;
                else
                {
                    simPoints[h]++;
                    simPoints[a]++;
                }
            }
            Array.Sort(order, ranking);
            for (var p = 0; p < n; p++) finish[order[p], p]++;
            for (var i = 0; i < n; i++) pointsSum[i] += simPoints[i];
        }

        // Puesto de cada uno en la clasificación de la jornada (sin sorteo: a igualdad, el orden de la lista).
        var table = Enumerable.Range(0, n)
            .OrderByDescending(i => points[i]).ThenByDescending(i => goalsFor[i] - goalsAgainst[i]).ThenByDescending(i => goalsFor[i]).ThenBy(i => i)
            .ToArray();
        var position = new int[n];
        for (var p = 0; p < n; p++) position[table[p]] = p + 1;

        double Share(int team, int from, int to)
        {
            long count = 0;
            for (var p = Math.Max(from, 0); p <= Math.Min(to, n - 1); p++) count += finish[team, p];
            return Math.Round(count / (double)simulations, 4);
        }

        return Enumerable.Range(0, n)
            .Select(i =>
            {
                var odds = Enumerable.Range(0, n).Select(p => Math.Round(finish[i, p] / (double)simulations, 4)).ToArray();
                var expectedPosition = Enumerable.Range(0, n).Sum(p => (p + 1) * finish[i, p] / (double)simulations);
                return new TeamForecast(
                    teams[i], position[i], points[i], games[i], goalsFor[i], goalsAgainst[i],
                    Share(i, 0, 0), Share(i, 0, zones.PromotionUntil - 1), zones.RelegationFrom is { } from ? Share(i, from - 1, n - 1) : 0,
                    Math.Round(pointsSum[i] / (double)simulations, 1), Math.Round(expectedPosition, 2), odds,
                    Math.Round(attack[i], 3), Math.Round(defense[i], 3));
            })
            .OrderBy(t => t.ExpectedPosition)
            .ToList();
    }

    // Goles medios del local y del visitante en la liga y fuerza de cada equipo (1 = la media): ataque, cuánto marca frente a
    // lo esperado contra sus rivales; defensa, cuánto encaja (menos de 1 es mejor). Se ajustan juntas unas cuantas vueltas
    // porque dependen del rival, y se encogen hacia 1 cuando hay pocos partidos.
    private static (double MuHome, double MuAway, double[] Attack, double[] Defense) Ratings(
        int n, IReadOnlyList<(int H, int A, int HG, int AG)> played)
    {
        var muHome = (played.Sum(m => m.HG) + LeaguePriorMatches * HomeGoalsPrior) / (played.Count + LeaguePriorMatches);
        var muAway = (played.Sum(m => m.AG) + LeaguePriorMatches * AwayGoalsPrior) / (played.Count + LeaguePriorMatches);
        var attack = Enumerable.Repeat(1.0, n).ToArray();
        var defense = Enumerable.Repeat(1.0, n).ToArray();
        if (played.Count == 0) return (muHome, muAway, attack, defense);

        var scored = new double[n];
        var conceded = new double[n];
        foreach (var (h, a, hg, ag) in played)
        {
            scored[h] += hg;
            conceded[h] += ag;
            scored[a] += ag;
            conceded[a] += hg;
        }

        for (var iteration = 0; iteration < RatingIterations; iteration++)
        {
            var expectedFor = new double[n];
            var expectedAgainst = new double[n];
            foreach (var (h, a, _, _) in played)
            {
                expectedFor[h] += muHome * defense[a];
                expectedAgainst[h] += muAway * attack[a];
                expectedFor[a] += muAway * defense[h];
                expectedAgainst[a] += muHome * attack[h];
            }
            for (var i = 0; i < n; i++)
            {
                attack[i] = (scored[i] + TeamPriorGoals) / (expectedFor[i] + TeamPriorGoals);
                defense[i] = (conceded[i] + TeamPriorGoals) / (expectedAgainst[i] + TeamPriorGoals);
            }
            var attackMean = attack.Average();
            var defenseMean = defense.Average();
            for (var i = 0; i < n; i++)
            {
                attack[i] /= attackMean;
                defense[i] /= defenseMean;
            }
        }
        return (muHome, muAway, attack, defense);
    }

    private static double Normal(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    private static int Poisson(Random random, double lambda)
    {
        var limit = Math.Exp(-lambda);
        var goals = 0;
        var product = random.NextDouble();
        while (product > limit)
        {
            goals++;
            product *= random.NextDouble();
        }
        return goals;
    }
}

// Plazas de la clasificación final que mira el previsor, las habituales de cada categoría (pueden cambiar con los arrastres
// de las categorías superiores): ascenso directo o promoción y descenso.
internal static class LeagueZoneRules
{
    public static ForecastZones For(Competition competition, int teams)
    {
        var (promotion, relegated) = (competition.Level, competition.Tier) switch
        {
            (CompetitionLevel.National, 1) => (5, 5),
            (CompetitionLevel.National, 2) => (5, 5),
            (CompetitionLevel.National, 3) => (5, 4),
            (CompetitionLevel.Regional, 1) => (2, 4),
            (CompetitionLevel.Regional, 2) => (2, 3),
            (CompetitionLevel.Regional, _) => (2, 0),
            _ => (1, 3),
        };
        relegated = Math.Min(relegated, Math.Max(0, teams / 4));
        promotion = Math.Clamp(promotion, 1, Math.Max(1, teams - relegated - 1));
        return new ForecastZones(promotion, relegated > 0 ? teams - relegated + 1 : null);
    }
}

// Las simulaciones son caras y no cambian mientras no cambien los partidos: se guardan por temporada, jornada y huella de
// los datos. Si se acumulan muchas, se empieza de cero.
internal sealed class LeagueForecastCache
{
    private const int MaxEntries = 300;
    private readonly ConcurrentDictionary<string, Lazy<object>> _entries = new();

    public T GetOrAdd<T>(string key, Func<T> compute) where T : class
    {
        if (_entries.Count > MaxEntries) _entries.Clear();
        return (T)_entries.GetOrAdd(key, _ => new Lazy<object>(() => compute())).Value;
    }
}
