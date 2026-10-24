using TercerTiempo.Domain.Common;

namespace TercerTiempo.Application.OfficialSheets;

internal static class TeamNameSimilarity
{
    private static readonly HashSet<string> Fillers =
    [
        "c", "d", "cd", "cf", "fc", "ud", "sd", "ad", "cp", "rc", "uc", "ei", "sad", "club", "de", "del", "la", "el", "los", "las", "y", "e",
        "deportivo", "deportiva", "futbol", "ff",
    ];

    public static readonly HashSet<char> Quotes = ['\'', '"', '«', '»', '“', '”', '‘', '’'];

    public static double Score(string a, string b)
    {
        var (coreA, suffixA) = Split(a);
        var (coreB, suffixB) = Split(b);

        if (coreA.Count == 0 || coreB.Count == 0) return 0;

        var intersection = coreA.Intersect(coreB).Count();
        if (intersection == 0) return 0;

        var jaccard = (double)intersection / coreA.Union(coreB).Count();

        // Que el nombre corto esté entero en el largo ("sporting c" en "real sporting gijon c") vale casi tanto como la igualdad.
        var containment = (double)intersection / Math.Min(coreA.Count, coreB.Count);
        var core = Math.Max(jaccard, containment * 0.9);

        var suffixFactor = suffixA == suffixB ? 1.0
            : suffixA is null || suffixB is null ? 0.85
            : 0.3;

        return Math.Round(core * suffixFactor, 3);
    }

    public static (HashSet<string> Core, string? Suffix) Split(string name)
    {
        var normalized = NameNormalizer.Normalize(name);
        var cleaned = new string(normalized.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        string? suffix = null;
        if (words.Count > 1 && words[^1].Length == 1 && char.IsLetter(words[^1][0]))
        {
            var quoted = normalized.Length > 1 && Quotes.Contains(normalized[^1]);
            if (quoted || words[^2].Length > 1)
            {
                suffix = words[^1];
                words.RemoveAt(words.Count - 1);
            }
        }

        var core = words.Where(w => !Fillers.Contains(w) && (w.Length > 1 || char.IsDigit(w[0]))).ToHashSet();
        return (core, suffix);
    }
}
