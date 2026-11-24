using System.Text.RegularExpressions;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Application.Squads.Injuries;

// El código de lo que crea el administrador en un catálogo no lo ve nadie: sale del nombre («Rotura de menisco» →
// RoturaDeMenisco) y no cambia aunque se renombre, porque es lo que guardan las lesiones.
internal static partial class CatalogCode
{
    public static string From(string label, IEnumerable<string> existing, int maxLength, string fallback)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = string.Concat(NonAlphanumeric().Split(NameNormalizer.Normalize(label))
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        if (stem.Length == 0) stem = fallback;
        stem = stem[..Math.Min(stem.Length, maxLength - 3)];

        var candidate = stem;
        for (var n = 2; taken.Contains(candidate); n++)
            candidate = $"{stem}{n}";
        return candidate;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
