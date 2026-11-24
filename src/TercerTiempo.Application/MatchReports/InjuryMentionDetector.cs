using System.Text;
using System.Text.RegularExpressions;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.MatchReports;

// Un jugador del acta oficial del partido, con lo que hace falta para reconocerlo en la crónica.
public sealed record ReportPlayer(MatchSide Side, string FullName, int? ShirtNumber, bool Played, int? MinuteOut);

public sealed record DetectedInjury(ReportPlayer Player, string Excerpt, string Phrase);

// Las frases de la crónica de un partido que cuentan que un jugador se lesionó. Precisión antes que cobertura: lo que
// sale es una propuesta que alguien confirma, pero una propuesta falsa molesta. Por eso:
//   - la frase tiene que decir que hubo lesión ("luxación", "se lesionó", "tuvo que retirarse", "camilla"...) y no que
//     venía de una ("tras su lesión", "reaparece", "baja por lesión") ni que se quedó en un susto;
//   - el jugador tiene que haber jugado según el acta y salir nombrado en esa frase, o en la anterior del mismo párrafo
//     si en esta no se nombra a nadie ("Diego Espina cayó mal. Tuvo que ser sustituido."): si antes de la lesión sale un
//     nombre que no está en el acta (un apodo: "Madeira sufrió una lesión"), no se le busca en la anterior;
//   - quien entra en su lugar ("fue sustituido por X", "le sustituyó X", "su relevo fue X") nunca es el lesionado;
//   - nombre y apellido, o los dos apellidos, valen siempre; un solo apellido o un solo nombre (o su diminutivo), solo
//     escritos con mayúscula y si nadie más del acta (de los dos equipos) se llama así;
//   - si se nombra a varios, el que dejó el campo (el que entra en su lugar sigue jugando) y, si no, el más cercano.
internal static class InjuryMentionDetector
{
    private const int MaxPhraseWords = 10;

    private const string Body =
        @"(tobillo|rodilla|codo|hombro|muneca|clavicula|tibia|perone|femur|cadera|gemelo|isquio\w*|aductor\w*|abductor\w*|cuadriceps"
        + @"|menisco|ligamento\w*|tendon|espalda|costilla\w*|nariz|tabique|mano|dedo|pie|brazo|pierna|cabeza|ceja|pomulo|mandibula"
        + @"|pubis|soleo|muslo|biceps|femoral\w*|lumbar\w*|cervical\w*)";

    // Sobre el texto sin tildes y en minúsculas. Peso 3: dice qué lesión; peso 2: solo que se fue lesionado.
    private static readonly (Regex Pattern, int Weight)[] Keywords =
    [
        (Pattern(@"\blesion\w*"), 3),
        (Pattern(@"\b(luxa\w*|luxo|disloc\w*)\b"), 3),
        (Pattern(@"\bfractur\w*"), 3),
        (Pattern(@"\besguince\w*"), 3),
        (Pattern(@"\bconmocion\w*"), 3),
        (Pattern(@"\bcontractura\w*"), 3),
        (Pattern(@"\bdesgarro\w*"), 3),
        (Pattern(@"\bdistension\w*"), 3),
        (Pattern(@"\belongacion\w*"), 3),
        (Pattern(@"\b(torcedura\w*|torcerse|se (le )?torcio)\b"), 3),
        (Pattern(@"\bpinchazo\w*"), 3),
        (Pattern($@"\brotura (fibrilar|muscular|de fibras|de ligamentos?|de menisco|del? tendon|(del?|de la|en el|en la) {Body})"), 3),
        (Pattern($@"\b(se )?(rompio|rompe) (el|la|los|las) {Body}"), 3),
        (Pattern(@"\b(camilla|ambulancia|hospital\w*|servicios medicos|asistencia medica)\b"), 2),
        (Pattern(@"\btuvo que (ser )?(retirad\w*|retirarse|abandonar|dejar el (campo|terreno)|sustituid\w*|cambiad\w*|atendid\w*|trasladad\w*)"), 2),
        (Pattern(@"\bse retiro (del campo |del terreno de juego )?(con molestias|dolorido|cojeando)"), 2),
        (Pattern(@"\b(molestias?|dolorido|doliendose|se dolia|cojeando|cojera|sobrecarga)\b"), 2),
    ];

    // Frases que hablan de una lesión que no es de este partido o que se quedó en nada.
    private static readonly Regex[] Exclusions =
    [
        Pattern(@"\btras (su|la|una) (larga )?lesion"),
        Pattern(@"\b(vuelve|volvia|volvio|regresa|regresaba|regreso|vuelta) (de|tras) (su |la |una )?(larga )?lesion"),
        Pattern(@"\brecuperad[oa]s? de\b"),
        Pattern(@"\b(reaparec\w*|reaparicion)\b"),
        Pattern(@"\b(bajas? (por|de) lesion|ausen\w* por lesion|lesionados|sin lesiones|sin molestias)\b"),
        Pattern(@"\bno (pudo|podra|pudieron|podran) (jugar|estar|contar)\b"),
        Pattern(@"\b(pudo|puede) (continuar|seguir)\b|\bsiguio jugando\b|\bsusto\b"),
    ];

    // Lo que va justo antes del nombre de quien entra en lugar del lesionado.
    private static readonly Regex Replacement = Pattern(
        @"\b((sustituid|reemplazad|relevad)[oa]s? por|(le |lo )?(sustituy[oe]|sustituia|reemplaz[oa]|relev[oa])|entr[oa]|ingres[oa]"
        + @"|(dio|da|dando) entrada a|(su|el) (relevo|sustituto|recambio|reemplazo) (fue|era|es)|(dejo|deja|dejando) (su )?(sitio|lugar|puesto) a"
        + @"|en su (lugar|puesto)( entro)?)[\s,:]+$");

    private static readonly Regex WordRegex = Pattern(@"[a-z0-9]+");
    private static readonly Regex SentenceBreak = new(@"(?<=[.!?…])\s+(?=[\p{Lu}¿¡""«(0-9])", RegexOptions.Compiled);
    private static readonly HashSet<string> Particles = ["de", "del", "la", "las", "los", "san", "da", "das", "do", "dos", "van", "von"];
    private static readonly char[] ClauseEnd = [',', '.', ';', ':', '(', ')', '!', '?', '«', '»', '"', '“', '”'];

    public static IReadOnlyList<DetectedInjury> Detect(string? text, IReadOnlyList<ReportPlayer> players)
    {
        var everyone = players.Select(ToPerson).ToList();
        var played = everyone.Where(p => p.Player.Played).ToList();
        if (played.Count == 0 || string.IsNullOrWhiteSpace(text)) return [];

        var best = new Dictionary<ReportPlayer, (DetectedInjury Injury, int Weight, int Order)>();
        var order = 0;
        foreach (var paragraph in Paragraphs(text))
        {
            for (var i = 0; i < paragraph.Count; i++, order++)
            {
                var sentence = paragraph[i];
                var folded = Fold(sentence);
                if (Exclusions.Any(e => e.IsMatch(folded))) continue;

                var found = Keywords.SelectMany(k => k.Pattern.Matches(folded).Select(m => (m.Index, k.Weight))).ToList();
                if (found.Count == 0) continue;
                var weight = found.Max(f => f.Weight);
                // La frase de la lesión empieza en la primera palabra que la cuenta: "tuvo que ser sustituido por un
                // esguince en el tobillo" dice más que "esguince en el tobillo".
                var start = found.Min(f => f.Index);

                var hits = Find(played, everyone, sentence, folded);
                var excerpt = sentence;
                if (hits.Count == 0 && i > 0 && !NamesSomeoneBefore(sentence, folded, start))
                {
                    var previous = Find(played, everyone, paragraph[i - 1], Fold(paragraph[i - 1]));
                    if (previous.Select(h => h.Person).Distinct().Count() == 1)
                    {
                        hits = previous;
                        excerpt = $"{paragraph[i - 1]} {sentence}";
                    }
                }
                if (Pick(hits, start) is not { } hit) continue;

                var player = hit.Person.Player;
                var known = best.TryGetValue(player, out var current);
                if (known && current.Weight >= weight) continue;
                best[player] = (new DetectedInjury(player, excerpt, Phrase(sentence, start)), weight, known ? current.Order : order);
            }
        }

        return best.Values.OrderBy(b => b.Order).Select(b => b.Injury).ToList();
    }

    // Párrafos (uno por línea) partidos en frases.
    internal static IReadOnlyList<IReadOnlyList<string>> Paragraphs(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => (IReadOnlyList<string>)SentenceBreak.Split(p).Select(s => s.Trim()).Where(s => s.Length > 0).ToList())
            .ToList();

    // Minúsculas y sin tildes, letra a letra: cada posición sigue siendo la del texto original.
    internal static string Fold(string text)
    {
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsSurrogate(c))
            {
                chars[i] = c;
                continue;
            }
            c = char.ToLowerInvariant(c);
            if (c > 127)
            {
                var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
                if (decomposed.Length > 0 && decomposed[0] < 128) c = decomposed[0];
            }
            chars[i] = c;
        }
        return new string(chars);
    }

    private sealed record Person(ReportPlayer Player, string[] Given, string[][] Surnames);

    private sealed record Hit(Person Person, int Index, bool Strong);

    private sealed record Token(string Word, int Index);

    // "APELLIDOS, NOMBRE" del acta: los apellidos agrupados con sus partículas ("de la Fuente").
    private static Person ToPerson(ReportPlayer player)
    {
        var comma = player.FullName.IndexOf(',');
        string[] given, surnames;
        if (comma > 0)
        {
            surnames = Words(player.FullName[..comma]);
            given = Words(player.FullName[(comma + 1)..]);
        }
        else
        {
            var words = Words(player.FullName);
            given = words.Take(1).ToArray();
            surnames = words.Skip(1).ToArray();
        }
        return new Person(player, given, Groups(surnames));
    }

    private static string[] Words(string text) => WordRegex.Matches(Fold(text)).Select(m => m.Value).ToArray();

    private static string[][] Groups(string[] words)
    {
        var groups = new List<string[]>();
        var current = new List<string>();
        foreach (var word in words)
        {
            current.Add(word);
            if (Particles.Contains(word)) continue;
            groups.Add([.. current]);
            current.Clear();
        }
        if (current.Count > 0)
        {
            if (groups.Count > 0) groups[^1] = [.. groups[^1], .. current];
            else groups.Add([.. current]);
        }
        return [.. groups];
    }

    private static List<Hit> Find(IReadOnlyList<Person> people, IReadOnlyList<Person> everyone, string sentence, string folded)
    {
        var tokens = WordRegex.Matches(folded).Select(m => new Token(m.Value, m.Index)).ToList();
        var hits = new List<Hit>();
        foreach (var person in people)
        {
            if (StrongAt(person, tokens) is { } strong) hits.Add(new Hit(person, strong, true));
            else if (WeakAt(person, everyone, tokens, sentence) is { } weak) hits.Add(new Hit(person, weak, false));
        }
        return hits.Where(h => !EntersAt(folded, h.Index)).ToList();
    }

    private static bool EntersAt(string folded, int index) => Replacement.IsMatch(folded[..index]);

    // Antes de la palabra de la lesión sale un nombre propio (con mayúscula, sin contar la primera palabra de la frase ni a
    // quien entra en su lugar): la frase habla de alguien, aunque no se sepa quién es.
    private static bool NamesSomeoneBefore(string sentence, string folded, int keywordIndex)
    {
        var tokens = WordRegex.Matches(folded).Where(m => m.Index < keywordIndex).ToList();
        for (var i = 1; i < tokens.Count; i++)
        {
            if (!char.IsUpper(sentence[tokens[i].Index])) continue;
            if (!EntersAt(folded, tokens[i].Index)) return true;
            // El nombre entero de quien entra: las palabras con mayúscula (y sus partículas) que siguen.
            while (i + 1 < tokens.Count && (char.IsUpper(sentence[tokens[i + 1].Index]) || Particles.Contains(tokens[i + 1].Value))) i++;
        }
        return false;
    }

    // Nombre (o diminutivo) y primer apellido, o los dos apellidos.
    private static int? StrongAt(Person person, List<Token> tokens)
    {
        if (person.Surnames.Length == 0) return null;
        var first = person.Surnames[0];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!SequenceAt(tokens, i, first)) continue;
            if (i > 0 && person.Given.Any(g => PersonName.SameGiven(tokens[i - 1].Word, g))) return tokens[i - 1].Index;
            if (person.Surnames.Length > 1 && SequenceAt(tokens, i + first.Length, person.Surnames[1])) return tokens[i].Index;
        }
        return null;
    }

    // Un solo apellido o un solo nombre: con mayúscula, sin otro nombre propio pegado que lo haga de otra persona
    // ("Pablo Espina", también a principio de frase) y sin nadie más en el acta que se llame así. A principio de frase
    // todo va con mayúscula, así que ahí un nombre suelto tiene que ser exacto (un diminutivo podría ser una palabra
    // cualquiera: "Este" y "Esteban").
    private static int? WeakAt(Person person, IReadOnlyList<Person> everyone, List<Token> tokens, string sentence)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var group in person.Surnames)
            {
                if (!SequenceAt(tokens, i, group)) continue;
                var main = tokens[i + group.Length - 1];
                if (!char.IsUpper(sentence[main.Index]) || string.Join(' ', group).Length < 4) continue;
                if (i > 0 && char.IsUpper(sentence[tokens[i - 1].Index]) && !person.Given.Any(g => PersonName.SameGiven(tokens[i - 1].Word, g))) continue;
                if (everyone.Any(o => o != person && (o.Surnames.Any(s => s.SequenceEqual(group)) || group.Length == 1 && o.Given.Contains(group[0])))) continue;
                return tokens[i].Index;
            }

            var word = tokens[i].Word;
            if (word.Length < 3 || person.Given.Length == 0 || !char.IsUpper(sentence[tokens[i].Index])) continue;
            var exact = person.Given.Contains(word);
            if (!exact && (i == 0 || !PersonName.SameGiven(word, person.Given[0]))) continue;
            if (i + 1 < tokens.Count && char.IsUpper(sentence[tokens[i + 1].Index])) continue;
            if (everyone.Any(o => o != person && (o.Given.Length > 0 && PersonName.SameGiven(word, o.Given[0]) || o.Surnames.Any(s => s.Length == 1 && s[0] == word)))) continue;
            return tokens[i].Index;
        }
        return null;
    }

    private static bool SequenceAt(List<Token> tokens, int start, string[] words)
    {
        if (words.Length == 0 || start < 0 || start + words.Length > tokens.Count) return false;
        for (var k = 0; k < words.Length; k++)
            if (tokens[start + k].Word != words[k]) return false;
        return true;
    }

    private static Hit? Pick(List<Hit> hits, int keywordIndex)
    {
        if (hits.Count == 0) return null;
        var pool = hits.Any(h => h.Strong) ? hits.Where(h => h.Strong).ToList() : hits;
        var left = pool.Where(h => h.Person.Player.MinuteOut is not null).ToList();
        if (left.Count > 0) pool = left;
        return pool.MinBy(h => Math.Abs(h.Index - keywordIndex));
    }

    // Desde la primera palabra de la lesión hasta el final de esa parte de la frase, como mucho unas diez palabras.
    private static string Phrase(string sentence, int start)
    {
        var end = sentence.IndexOfAny(ClauseEnd, start);
        var clause = (end < 0 ? sentence[start..] : sentence[start..end]).Trim();
        var phrase = string.Join(' ', clause.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(MaxPhraseWords));
        return phrase.Length <= MatchInjuryMention.MaxPhraseLength ? phrase : phrase[..MatchInjuryMention.MaxPhraseLength].TrimEnd();
    }

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
