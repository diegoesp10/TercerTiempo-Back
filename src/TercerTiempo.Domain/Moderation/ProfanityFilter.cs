using System.Globalization;
using System.Text;

namespace TercerTiempo.Domain.Moderation;

// Busca palabras y expresiones prohibidas en un texto. Compara palabras enteras (así «disputa» no es «puta» ni «computadora»
// tiene nada), sin mayúsculas ni tildes salvo la ñ («coño» no es «cono»), y deshace los disfraces habituales: números y
// símbolos por letras («p0ll4», «put@»), letras repetidas («putaaa»), el asterisco como comodín («p*ta») y letras sueltas
// seguidas («p u t a», «p.u.t.a»).
public static class ProfanityFilter
{
    private static readonly Dictionary<char, char> Disguises = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['@'] = 'a', ['$'] = 's', ['€'] = 'e',
    };

    public static string Normalize(string? text) => string.Join(' ', Words(text));

    public static IReadOnlyList<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var letter in text.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (letter == 'ñ')
            {
                word.Append(letter);
                continue;
            }
            foreach (var ch in letter.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch) || ch == '*' || Disguises.ContainsKey(ch)) word.Append(ch);
                else Flush();
            }
        }
        Flush();
        return words;

        void Flush()
        {
            if (word.Length == 0) return;
            var clean = Undisguise(word.ToString());
            if (clean.Length > 0) words.Add(clean);
            word.Clear();
        }
    }

    // Las palabras o expresiones de la lista que aparecen en el texto, en el orden de la lista.
    public static IReadOnlyList<string> Find(string? text, IEnumerable<string> terms)
    {
        var words = Words(text);
        if (words.Count == 0) return [];

        var single = new HashSet<string>(words, StringComparer.Ordinal);
        foreach (var spelled in Spelled(words)) single.Add(spelled);
        var wildcards = single.Where(w => w.Contains('*') && w.Count(char.IsLetter) >= 2).ToList();
        var line = $" {string.Join(' ', words)} ";

        var found = new List<string>();
        foreach (var term in terms)
        {
            var hit = term.Contains(' ')
                ? line.Contains($" {term} ", StringComparison.Ordinal)
                : Forms(term).Any(form => single.Contains(form) || wildcards.Any(w => Fits(w, form)));
            if (hit && !found.Contains(term)) found.Add(term);
        }

        // Sin repetirse: «hijo de puta» ya dice «puta», y «gilipollas» (lo escrito) ya dice «gilipolla».
        return found.Where(term => !found.Any(other => other != term && Covers(other, term))).ToList();
    }

    private static bool Covers(string other, string term) =>
        other.Contains(' ') ? $" {other} ".Contains($" {term} ", StringComparison.Ordinal) : other == term + "s" || other == term + "es";

    // Una palabra suelta también en plural.
    private static IEnumerable<string> Forms(string term) => [term, term + "s", term + "es"];

    private static bool Fits(string pattern, string word) =>
        pattern.Length == word.Length && pattern.Zip(word).All(p => p.First == '*' || p.First == p.Second);

    // Números y símbolos dentro de una palabra con letras pasan a letra («h1j0» → hijo); los números sueltos se quedan
    // («2-2»). Tres o más letras iguales seguidas cuentan como una («mieeerda»), pero no dos («perro», «calle»).
    private static string Undisguise(string word)
    {
        var withLetters = word.Any(char.IsLetter);
        var builder = new StringBuilder(word.Length);
        foreach (var ch in word)
        {
            if (Disguises.TryGetValue(ch, out var letter) && withLetters) builder.Append(letter);
            else if (char.IsLetterOrDigit(ch) || ch == '*') builder.Append(ch);
        }

        var clean = new StringBuilder(builder.Length);
        for (var i = 0; i < builder.Length; i++)
        {
            var run = 1;
            while (i + run < builder.Length && builder[i + run] == builder[i]) run++;
            clean.Append(builder[i], run >= 3 && char.IsLetter(builder[i]) ? 1 : run);
            i += run - 1;
        }
        return clean.ToString();
    }

    // Letras sueltas seguidas se leen también juntas: «p u t a» → puta.
    private static IEnumerable<string> Spelled(IReadOnlyList<string> words)
    {
        var run = new StringBuilder();
        foreach (var word in words.Append(string.Empty))
        {
            if (word.Length == 1 && (char.IsLetter(word[0]) || word[0] == '*'))
            {
                run.Append(word);
                continue;
            }
            if (run.Length >= 3) yield return run.ToString();
            run.Clear();
        }
    }
}
