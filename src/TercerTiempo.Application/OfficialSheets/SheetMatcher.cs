using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.OfficialSheets;

internal static class SheetMatcher
{
    private const double MinScore = 0.6;

    public sealed record Candidate(Guid Id, string NormalizedName, int? ShirtNumber);

    public static IReadOnlyDictionary<Guid, OfficialSheetLine> Match(IEnumerable<SquadPlayer> players, IEnumerable<OfficialSheetLine> lines) =>
        Match(players.Select(p => new Candidate(p.Id, p.NormalizedName, p.ShirtNumber)), lines);

    public static IReadOnlyDictionary<Guid, OfficialSheetLine> Match(IEnumerable<Candidate> players, IEnumerable<OfficialSheetLine> lines)
    {
        var playerTokens = players.Select(p => (Player: p, Tokens: Tokens(p.NormalizedName))).ToList();
        var lineTokens = lines.Select(l => (Line: l, Tokens: Tokens(l.NormalizedName))).ToList();

        var candidates = new List<(double Score, Candidate Player, OfficialSheetLine Line)>();

        foreach (var (player, pTokens) in playerTokens)
        {
            foreach (var (line, lTokens) in lineTokens)
            {
                var score = Math.Max(
                    Score(pTokens, lTokens, player.ShirtNumber, line.ShirtNumber),
                    SurnameScore(pTokens, line.FullName, player.ShirtNumber, line.ShirtNumber));
                if (score >= MinScore)
                    candidates.Add((score, player, line));
            }
        }

        var result = new Dictionary<Guid, OfficialSheetLine>();
        var usedLines = new HashSet<Guid>();

        foreach (var (_, player, line) in candidates.OrderByDescending(c => c.Score))
        {
            if (result.ContainsKey(player.Id) || usedLines.Contains(line.Id)) continue;
            result[player.Id] = line;
            usedLines.Add(line.Id);
        }

        // Una sola palabra ("Mazo", "Juan"; las actas de menores solo traen el nombre de pila) no basta por sí misma,
        // salvo que solo un jugador libre la lleve en su nombre y ninguna otra línea suelta diga lo mismo: si hay dos
        // Mateos en la plantilla y uno ya está cruzado con su línea completa, el "Mateo" que queda es el otro.
        var single = lineTokens.Where(l => l.Tokens.Count == 1 && !usedLines.Contains(l.Line.Id)).ToList();
        foreach (var (line, lTokens) in single)
        {
            var token = lTokens.First();
            if (token.Length < 3) continue;
            if (single.Count(l => l.Tokens.First() == token) != 1) continue;
            var owners = playerTokens.Where(p => !result.ContainsKey(p.Player.Id) && p.Tokens.Any(o => Covers(token, o))).ToList();
            if (owners.Count != 1) continue;
            result[owners[0].Player.Id] = line;
            usedLines.Add(line.Id);
        }

        return result;
    }

    public static IReadOnlyDictionary<Guid, SquadPlayer> MatchPlayers(IEnumerable<Candidate> entries, IEnumerable<SquadPlayer> players)
    {
        var entryTokens = entries.Select(e => (Entry: e, Tokens: Tokens(e.NormalizedName))).ToList();
        var playerTokens = players.Select(p => (Player: p, Tokens: Tokens(p.NormalizedName))).ToList();

        var candidates = new List<(double Score, Candidate Entry, SquadPlayer Player)>();
        foreach (var (entry, eTokens) in entryTokens)
        {
            foreach (var (player, pTokens) in playerTokens)
            {
                var score = Score(eTokens, pTokens, entry.ShirtNumber, player.ShirtNumber);
                if (score >= MinScore) candidates.Add((score, entry, player));
            }
        }

        var result = new Dictionary<Guid, SquadPlayer>();
        var used = new HashSet<Guid>();
        foreach (var (_, entry, player) in candidates.OrderByDescending(c => c.Score))
        {
            if (result.ContainsKey(entry.Id) || used.Contains(player.Id)) continue;
            result[entry.Id] = player;
            used.Add(player.Id);
        }

        return result;
    }

    internal static double Score(HashSet<string> player, HashSet<string> line, int? playerShirt, int? lineShirt)
    {
        if (player.Count == 0 || line.Count == 0) return 0;

        var shirtBonus = playerShirt is { } ps && lineShirt is { } ls && ps == ls ? 0.3 : 0;

        if (player.SetEquals(line)) return 1.0 + shirtBonus;

        var shorter = player.Count <= line.Count ? player : line;
        var longer = ReferenceEquals(shorter, player) ? line : player;

        // Nombre de pila solo ("Mateo") o apellidos sin segundo nombre: todas las palabras del corto están en el largo.
        // También vale la inicial ("M. Huelga") o el diminutivo que empieza la palabra ("Guille" por Guillermo), como
        // escriben algunas webs, siempre que al menos una palabra no sea una inicial.
        if (shorter.All(t => longer.Any(o => Covers(t, o))) && shorter.Any(t => t.Length > 1))
            return (shorter.Count == 1 ? 0.45 : 0.75) + shirtBonus;

        var common = player.Intersect(line).Count();
        var jaccard = (double)common / player.Union(line).Count();
        return (common >= 2 ? Math.Max(jaccard, 0.5) : jaccard * 0.5) + shirtBonus;
    }

    // Las actas de la federación escriben "APELLIDOS, NOMBRE" y los apellidos son lo que no cambia: las plantillas ponen
    // apodos o diminutivos ("Nacho Cabal" por "CABAL GONZALEZ, IGNACIO", "Berto Arias" por "ARIAS SANZ, ALBERTO JESUS"),
    // a veces un solo apellido y alguna errata ("Liñera" por "LIÑEIRA"). Hace falta el primer apellido y que lo demás
    // del nombre sea el otro apellido o encaje con el nombre de pila; un nombre distinto ("Pablo Cabal") solo vale si
    // además coincide el dorsal.
    internal static double SurnameScore(HashSet<string> player, string lineFullName, int? playerShirt, int? lineShirt)
    {
        var comma = lineFullName.IndexOf(',');
        if (comma <= 0 || player.Count < 2) return 0;

        var surnames = Tokens(NameNormalizer.Normalize(lineFullName[..comma])).ToList();
        var given = Tokens(NameNormalizer.Normalize(lineFullName[(comma + 1)..]));
        if (surnames.Count == 0 || surnames[0].Length < 3 || !player.Any(p => SameSurname(p, surnames[0]))) return 0;

        var secondSurname = surnames.Skip(1).Any(s => player.Any(p => SameSurname(p, s)));
        var givenName = given.Any(g => player.Any(p => SameGiven(p, g)));
        var unexplained = player.Count(p => !surnames.Any(s => SameSurname(p, s)) && !given.Any(g => SameGiven(p, g)));

        var shirtBonus = playerShirt is { } ps && lineShirt is { } ls && ps == ls ? 0.3 : 0;
        if (unexplained > 0) return unexplained == 1 && shirtBonus > 0 ? 0.5 + shirtBonus : 0;
        return 0.5 + (secondSurname ? 0.25 : 0) + (givenName ? 0.2 : 0) + shirtBonus;
    }

    private static bool SameSurname(string token, string surname) =>
        token == surname || token.Length >= 5 && surname.Length >= 5 && Math.Abs(token.Length - surname.Length) <= 1 && EditDistance(token, surname) <= 1;

    // Nombre de pila, inicial, diminutivo que empieza o acaba igual ("Guille", "Ivanchu", "Berto") o apodo conocido.
    private static bool SameGiven(string token, string name) =>
        token == name
        || token.Length == 1 && name[0] == token[0]
        || token.Length >= 4 && (name.StartsWith(token, StringComparison.Ordinal) || name.EndsWith(token, StringComparison.Ordinal))
        || name.Length >= 4 && token.StartsWith(name, StringComparison.Ordinal)
        || Nicknames.TryGetValue(token, out var names) && names.Contains(name);

    private static readonly Dictionary<string, string[]> Nicknames = new()
    {
        ["nacho"] = ["ignacio"], ["inaki"] = ["ignacio"], ["pepe"] = ["jose"], ["pepin"] = ["jose"], ["chema"] = ["jose", "maria"],
        ["josema"] = ["jose", "maria"], ["juanjo"] = ["juan", "jose"], ["juanma"] = ["juan", "manuel"], ["paco"] = ["francisco"],
        ["curro"] = ["francisco"], ["pancho"] = ["francisco"], ["quique"] = ["enrique"], ["kike"] = ["enrique"], ["lolo"] = ["manuel"],
        ["manolo"] = ["manuel"], ["chus"] = ["jesus"], ["suso"] = ["jesus"], ["chuchi"] = ["jesus"], ["toni"] = ["antonio"],
        ["tony"] = ["antonio"], ["tono"] = ["antonio"], ["lucho"] = ["luis"], ["alex"] = ["alejandro", "alexander", "alexis"],
        ["edu"] = ["eduardo"], ["willy"] = ["guillermo"], ["moi"] = ["moises"], ["charly"] = ["carlos"], ["kiko"] = ["francisco", "enrique"],
        ["fito"] = ["adolfo", "rodolfo"], ["tete"] = ["jose"], ["pachi"] = ["francisco"], ["rulo"] = ["raul"],
    };

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }

    private static bool Covers(string token, string other) =>
        token == other
        || token.Length == 1 && other.Length > 1 && other[0] == token[0]
        || token.Length >= 4 && other.Length > token.Length && other.StartsWith(token, StringComparison.Ordinal);

    internal static HashSet<string> Tokens(string normalizedName) =>
        normalizedName.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    public static List<SheetDifferenceDto> Differences(PlayerMatchPerformance? coach, OfficialSheetLine? official)
    {
        var result = new List<SheetDifferenceDto>();

        var coachPlayed = coach is not null && coach.MinutesPlayed > 0;
        var officialPlayed = official?.Played ?? false;

        if (coachPlayed != officialPlayed)
        {
            result.Add(new SheetDifferenceDto("played", Yes(coachPlayed), official is null ? null : Yes(officialPlayed)));
            return result;
        }

        if (coach is null || official is null || !coachPlayed)
            return result;

        if (coach.Started != official.Started)
            result.Add(new SheetDifferenceDto("started", Yes(coach.Started), Yes(official.Started)));

        if (coach.MinutesPlayed != official.MinutesPlayed)
            result.Add(new SheetDifferenceDto("minutes", coach.MinutesPlayed.ToString(), official.MinutesPlayed.ToString()));

        var officialGoals = official.Goals + official.PenaltyGoals;
        if (coach.Goals != officialGoals)
            result.Add(new SheetDifferenceDto("goals", coach.Goals.ToString(), officialGoals.ToString()));

        if (coach.YellowCards != official.YellowCards)
            result.Add(new SheetDifferenceDto("yellowCards", coach.YellowCards.ToString(), official.YellowCards.ToString()));

        if (coach.RedCard != official.RedCard)
            result.Add(new SheetDifferenceDto("redCard", Yes(coach.RedCard), Yes(official.RedCard)));

        return result;
    }

    private static string Yes(bool value) => value ? "sí" : "no";
}
