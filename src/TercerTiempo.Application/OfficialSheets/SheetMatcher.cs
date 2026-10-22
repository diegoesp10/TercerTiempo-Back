using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.OfficialSheets;

internal static class SheetMatcher
{
    private const double MinScore = 0.6;

    public static IReadOnlyDictionary<Guid, OfficialSheetLine> Match(IEnumerable<SquadPlayer> players, IEnumerable<OfficialSheetLine> lines)
    {
        var playerTokens = players.Select(p => (Player: p, Tokens: Tokens(p.NormalizedName))).ToList();
        var lineTokens = lines.Select(l => (Line: l, Tokens: Tokens(l.NormalizedName))).ToList();

        var candidates = new List<(double Score, SquadPlayer Player, OfficialSheetLine Line)>();

        foreach (var (player, pTokens) in playerTokens)
        {
            foreach (var (line, lTokens) in lineTokens)
            {
                var score = Score(pTokens, lTokens, player.ShirtNumber, line.ShirtNumber);
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
        if (shorter.IsSubsetOf(longer))
            return (shorter.Count == 1 ? 0.45 : 0.75) + shirtBonus;

        var common = player.Intersect(line).Count();
        var jaccard = (double)common / player.Union(line).Count();
        return (common >= 2 ? Math.Max(jaccard, 0.5) : jaccard * 0.5) + shirtBonus;
    }

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
