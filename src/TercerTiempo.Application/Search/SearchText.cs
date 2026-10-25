using System.Text;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Application.Search;

// Texto del buscador: sin mayúsculas, tildes ni signos («Caudal Deportivo ‘B’» → caudal, deportivo, b), y comparación
// tolerante (prefijos y erratas) entre lo que se escribe y las palabras del índice.
internal static class SearchText
{
    // Palabras que no distinguen nada por sí solas: no cuentan para exigir que coincida toda la búsqueda.
    private static readonly HashSet<string> StopWords = ["de", "del", "la", "las", "el", "los", "y", "e", "en", "vs", "contra"];

    // Siglas de tipo de club: cuentan, pero poco (casi todos los equipos tienen una).
    private static readonly HashSet<string> Generic = ["cd", "cf", "ud", "sd", "ad", "sad", "club", "fc", "cp", "sc", "rc", "cultural", "deportiva", "deportivo"];

    public static string Normalize(string? value)
    {
        var normalized = NameNormalizer.Normalize(value);
        if (normalized.Length == 0) return normalized;

        var builder = new StringBuilder(normalized.Length);
        var lastWasSpace = false;
        foreach (var ch in normalized)
        {
            // «3ª» y «3º» se quedan en 3 (ver Ordinals).
            if (ch is 'ª' or 'º') continue;
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && ch is not ('\'' or '‘' or '’' or '´' or '`'))
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    // «1a» o «3o» (ordinales escritos sin el símbolo) cuentan como el número.
    public static string[] Tokens(string? value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Length >= 2 && t[^1] is 'a' or 'o' && t[..^1].All(char.IsDigit) ? t[..^1] : t)
            .Distinct()
            .ToArray();

    // Palabras de la búsqueda que hay que encontrar; si todo son palabras vacías, se buscan tal cual.
    public static string[] QueryTokens(string? query)
    {
        var tokens = Tokens(query);
        var meaningful = tokens.Where(t => !StopWords.Contains(t)).ToArray();
        return meaningful.Length > 0 ? meaningful : tokens;
    }

    public static bool IsGeneric(string token) => Generic.Contains(token);

    // Las categorías se escriben con número («3ª RFEF») o con letra («tercera rfef»): el índice guarda las dos.
    private static readonly string[] Ordinals = ["primera", "segunda", "tercera", "cuarta", "quinta"];

    public static string[] WithOrdinals(string[] tokens) =>
        tokens.Concat(tokens.Where(t => t.Length == 1 && t[0] is >= '1' and <= '5').Select(t => Ordinals[t[0] - '1'])).Distinct().ToArray();

    // Parecido entre una palabra de la búsqueda y una del índice (0 = nada; 1 = idéntica).
    public static double Similarity(string query, string word)
    {
        if (query.Length == 0 || word.Length == 0) return 0;
        if (query == word) return 1;

        if (word.StartsWith(query, StringComparison.Ordinal))
            return query.Length == 1 ? (word.Length <= 2 ? 0.55 : 0) : 0.75 + 0.2 * query.Length / word.Length;

        // Un número (dorsal, jornada) solo vale exacto.
        if (query.All(char.IsDigit) || word.All(char.IsDigit)) return 0;

        if (query.Length >= 3 && word.Contains(query, StringComparison.Ordinal)) return 0.45;

        // Erratas: una de cada cuatro letras como mucho, contra la palabra entera o contra su comienzo (se escribe a medias).
        if (query.Length < 4) return 0;
        var allowed = query.Length >= 8 ? 2 : 1;
        if (Math.Abs(word.Length - query.Length) <= allowed && Distance(query, word, allowed) <= allowed) return 0.6;
        if (word.Length > query.Length && Distance(query, word[..query.Length], allowed) <= allowed) return 0.5;
        return 0;
    }

    // Distancia de edición (con trasposiciones) que deja de calcular al pasar del máximo.
    internal static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var previous2 = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, previous2[j - 2] + 1);
                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }
            if (rowMin > max) return max + 1;
            (previous2, previous, current) = (previous, current, previous2);
        }
        return previous[b.Length];
    }
}
