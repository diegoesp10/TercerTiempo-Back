namespace TercerTiempo.Domain.Common;

// Cuándo dos nombres son de la misma persona. Trabaja con nombres ya normalizados (NameNormalizer): sin tildes y en
// minúsculas.
public static class PersonName
{
    public static string[] Words(string normalizedName) =>
        normalizedName.Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Un nombre de acta oficial («APELLIDOS, NOMBRE») en el orden de una plantilla («NOMBRE APELLIDOS»); sin coma, igual.
    public static string FromSheet(string fullName)
    {
        var comma = fullName.IndexOf(',');
        return comma <= 0 ? fullName.Trim() : $"{fullName[(comma + 1)..].Trim()} {fullName[..comma].Trim()}".Trim();
    }

    // ¿Es la misma persona un nombre de acta oficial y uno de la app (ya normalizado)?
    public static bool SameAsSheet(string sheetFullName, string normalizedName) =>
        SamePerson(NameNormalizer.Normalize(FromSheet(sheetFullName)), normalizedName);

    // Dos nombres escritos como en una plantilla ("Nombre Apellido Apellido") son de la misma persona si el nombre de
    // pila es el mismo (o su diminutivo o apodo) y lo demás del más corto está, en el mismo orden, en el más largo, que
    // solo añade un segundo nombre o un segundo apellido: "Lucas Paredes" y "Lucas Paredes Rivera", "Álex Cueva" y
    // "Alejandro Cueva Vilaboy", "Mateo Álvarez" y "Mateo José Álvarez Álvarez". Un segundo apellido distinto ("Juan
    // García López" y "Juan García Pérez") son dos personas, y una sola palabra no dice de quién es: solo vale idéntica.
    public static bool SamePerson(string normalizedA, string normalizedB)
    {
        var a = Words(normalizedA);
        var b = Words(normalizedB);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a.Length == 1 || b.Length == 1) return a.SequenceEqual(b);

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (!SameGiven(shorter[0], longer[0]) && !SameGiven(longer[0], shorter[0])) return false;

        var next = 1;
        foreach (var word in shorter.Skip(1))
        {
            while (next < longer.Length && !SameSurname(word, longer[next])) next++;
            if (next == longer.Length) return false;
            next++;
        }

        return true;
    }

    // El mismo apellido, con una errata como mucho si es largo ("Liñera" por "Liñeira").
    public static bool SameSurname(string word, string surname) =>
        word == surname || word.Length >= 5 && surname.Length >= 5 && Math.Abs(word.Length - surname.Length) <= 1 && EditDistance(word, surname) <= 1;

    // Nombre de pila, inicial, diminutivo que empieza o acaba igual ("Guille", "Ivanchu", "Berto") o apodo conocido,
    // esté el apodo en un lado o en el otro ("Ignacio Puente" por "PUENTE GUTIERREZ, NACHO").
    public static bool SameGiven(string word, string name) =>
        word == name
        || word.Length == 1 && name[0] == word[0]
        || word.Length >= 4 && (name.StartsWith(word, StringComparison.Ordinal) || name.EndsWith(word, StringComparison.Ordinal))
        || name.Length >= 4 && word.StartsWith(name, StringComparison.Ordinal)
        || Nicknames.TryGetValue(word, out var names) && names.Contains(name)
        || Nicknames.TryGetValue(name, out var formal) && formal.Contains(word);

    private static readonly Dictionary<string, string[]> Nicknames = new()
    {
        ["nacho"] = ["ignacio"], ["inaki"] = ["ignacio"], ["pepe"] = ["jose"], ["pepin"] = ["jose"], ["chema"] = ["jose", "maria"],
        ["josema"] = ["jose", "maria"], ["juanjo"] = ["juan", "jose"], ["juanma"] = ["juan", "manuel"], ["paco"] = ["francisco"],
        ["curro"] = ["francisco"], ["pancho"] = ["francisco"], ["quique"] = ["enrique"], ["kike"] = ["enrique"], ["lolo"] = ["manuel"],
        ["manolo"] = ["manuel"], ["chus"] = ["jesus"], ["suso"] = ["jesus"], ["chuchi"] = ["jesus"], ["toni"] = ["antonio"],
        ["tony"] = ["antonio"], ["tono"] = ["antonio"], ["lucho"] = ["luis"], ["alex"] = ["alejandro", "alexander", "alexis"],
        ["edu"] = ["eduardo"], ["willy"] = ["guillermo"], ["moi"] = ["moises"], ["charly"] = ["carlos"], ["kiko"] = ["francisco", "enrique"],
        ["fito"] = ["adolfo", "rodolfo"], ["tete"] = ["jose"], ["pachi"] = ["francisco"], ["rulo"] = ["raul"],
        ["fonsi"] = ["alfonso"], ["fer"] = ["fernando"],
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
}
