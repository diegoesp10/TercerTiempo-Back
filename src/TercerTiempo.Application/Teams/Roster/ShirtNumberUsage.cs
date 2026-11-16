using System.ComponentModel;

namespace TercerTiempo.Application.Teams.Roster;

public sealed record ShirtNumberUsageDto(
    int Number,
    [property: Description("Partidos en los que consta con este dorsal (titular o suplente).")]
    int Matches,
    [property: Description("Porcentaje, sobre sus partidos con dorsal conocido, en los que llevó este (0-100; entre todos suman 100).")]
    int Percentage);

internal static class ShirtNumberUsage
{
    // Del más usado al menos; a igualdad, el más reciente. Los porcentajes se reparten por restos mayores para que
    // sumen 100.
    public static IReadOnlyList<ShirtNumberUsageDto> From(IEnumerable<(int Number, DateTimeOffset KickoffAt)> worn)
    {
        var used = worn
            .GroupBy(w => w.Number)
            .Select(g => (Number: g.Key, Matches: g.Count(), Last: g.Max(w => w.KickoffAt)))
            .OrderByDescending(u => u.Matches)
            .ThenByDescending(u => u.Last)
            .ToList();
        if (used.Count == 0) return [];

        var total = used.Sum(u => u.Matches);
        var percentages = used.Select(u => u.Matches * 100 / total).ToArray();
        var pending = 100 - percentages.Sum();
        foreach (var index in Enumerable.Range(0, used.Count).OrderByDescending(i => used[i].Matches * 100 % total).Take(pending))
            percentages[index]++;

        return used.Select((u, i) => new ShirtNumberUsageDto(u.Number, u.Matches, percentages[i])).ToList();
    }
}
