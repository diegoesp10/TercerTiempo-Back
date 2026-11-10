using TercerTiempo.Domain.Common;

namespace TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

public static class TeamNames
{
    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "c.d", "u.d", "u.p", "s.d", "c.f", "f.c", "e.i", "u.c", "c.p", "a.d", "r.c",
        "club", "real", "atlético", "atletico", "athletic", "deportivo", "deportiva", "sporting", "racing", "unión", "union",
        "de", "del", "la", "el", "los", "las", "y", "e",
    };

    public static string ShortName(string name, string? abbreviation)
    {
        if (!string.IsNullOrWhiteSpace(abbreviation))
        {
            var abbr = abbreviation.Trim().ToUpperInvariant();
            return abbr.Length <= 5 ? abbr : abbr[..5];
        }

        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.Trim('.', ',', '-', '«', '»', '(', ')', '"'))
            .Where(w => w.Length > 0)
            .ToList();

        // Primero sin siglas de club (CD, UD, CP, AD, RC, B...) ni palabras genéricas; si no queda nada, con todas.
        var significant = words.Where(w => !IsFiller(w)).ToList();
        var candidates = significant.Count > 0 ? significant : words;

        var source = candidates.FirstOrDefault(w => Letters(w).Length >= 3)
            ?? candidates.FirstOrDefault()
            ?? name;

        var letters = Letters(source);
        if (letters.Length == 0) letters = "EQP";

        return (letters.Length >= 3 ? letters[..3] : letters.PadRight(3, 'X')).ToUpperInvariant();
    }

    private static bool IsFiller(string word) =>
        Fillers.Contains(word) || (word.Length <= 3 && word.All(char.IsUpper));

    private static string Letters(string word) => new(NameNormalizer.Normalize(word).Where(char.IsLetter).ToArray());
}
