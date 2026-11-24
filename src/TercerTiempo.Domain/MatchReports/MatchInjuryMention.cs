using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Domain.MatchReports;

// Lo que encontró la detección en la crónica de un partido: quién se lesionó (con su nombre como en el acta), en qué
// minuto salió del campo según el acta y la frase de la crónica que lo cuenta. De la crónica solo se guardan esas frases,
// nunca el texto entero. Es público (la crónica lo es): vive en la base de datos reales y cada entorno tiene su copia, de
// la que salen las propuestas de lesión para el cuerpo técnico.
public sealed class MatchInjuryMention : AggregateRoot
{
    public const int MaxNameLength = 150;
    public const int MaxExcerptLength = 600;
    public const int MaxPhraseLength = 120;
    public const int MaxUrlLength = 300;

    public Guid MatchId { get; private set; }
    public MatchSide Side { get; private set; }
    // Como en el acta oficial: "APELLIDOS, NOMBRE".
    public string PlayerName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public int? ShirtNumber { get; private set; }
    // El minuto en que dejó el campo según el acta; null si acabó el partido.
    public int? Minute { get; private set; }
    public string Excerpt { get; private set; } = string.Empty;
    // Lo que le pasó, tal como lo dice la crónica: "luxación en su codo izquierdo".
    public string Phrase { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string SourceUrl { get; private set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MatchInjuryMention() { }

    public static MatchInjuryMention Create(
        Guid matchId, MatchSide side, string playerName, int? shirtNumber, int? minute, string excerpt, string phrase,
        string provider, string sourceUrl, DateTimeOffset now)
    {
        var name = Guard.NotEmpty(playerName, "jugador", MaxNameLength);
        var mention = new MatchInjuryMention
        {
            Id = NewId(),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            Side = side,
            PlayerName = name,
            // Como las líneas de las actas: sin la coma.
            NormalizedName = NameNormalizer.Normalize(name.Replace(',', ' ')),
            Provider = Guard.NotEmpty(provider, nameof(provider), 50),
            DetectedAt = now,
        };
        mention.Apply(shirtNumber, minute, excerpt, phrase, sourceUrl, now);
        return mention;
    }

    // Lo que dice ahora la crónica (puede corregirse después del partido); devuelve si ha cambiado algo.
    public bool Apply(int? shirtNumber, int? minute, string excerpt, string phrase, string sourceUrl, DateTimeOffset now)
    {
        var cleanExcerpt = Cut(Guard.NotEmpty(excerpt, "frase", 10_000), MaxExcerptLength);
        var cleanPhrase = Cut(Guard.NotEmpty(phrase, "lesión", 10_000), MaxPhraseLength);
        var cleanUrl = Cut(sourceUrl.Trim(), MaxUrlLength);
        if (shirtNumber == ShirtNumber && minute == Minute && cleanExcerpt == Excerpt && cleanPhrase == Phrase && cleanUrl == SourceUrl)
            return false;

        ShirtNumber = shirtNumber;
        Minute = minute is { } m ? Guard.InRange(m, 0, 200, "minuto") : null;
        Excerpt = cleanExcerpt;
        Phrase = cleanPhrase;
        SourceUrl = cleanUrl;
        UpdatedAt = now;
        return true;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}

// Cuándo se leyó por última vez la crónica de un partido y si ya la había. Es estado de la descarga: solo vive en la base
// de datos reales.
public sealed class MatchReportCheck
{
    public Guid MatchId { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }
    public int Attempts { get; private set; }
    // Cuándo apareció la crónica; null mientras el partido no la tenga.
    public DateTimeOffset? FoundAt { get; private set; }
    // Ya leída dos veces (al aparecer y un día después, por si la completan): no se vuelve a pedir.
    public bool Done { get; private set; }

    private MatchReportCheck() { }

    public static MatchReportCheck First(Guid matchId, bool found, DateTimeOffset now) =>
        new() { MatchId = Guard.NotEmpty(matchId, nameof(matchId)), CheckedAt = now, Attempts = 1, FoundAt = found ? now : null };

    // Con la crónica ya encontrada, esta es la segunda lectura: no hay más (aunque ahora no esté, lo leído vale).
    public void Again(bool found, DateTimeOffset now)
    {
        if (FoundAt is not null) Done = true;
        else if (found) FoundAt = now;
        CheckedAt = now;
        Attempts++;
    }

    // Sin la crónica, se deja de buscar al cabo de unos intentos: muchos partidos nunca la tienen.
    public void GiveUp() => Done = true;
}

public interface IMatchReportStore
{
    // Las de esos partidos, para modificarlas (la descarga).
    Task<IReadOnlyList<MatchInjuryMention>> GetStoredByMatchesAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken = default);

    // Las detectadas o cambiadas desde ese momento, solo para leer.
    Task<IReadOnlyList<MatchInjuryMention>> GetUpdatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    // Partidos jugados de ese proveedor con el acta oficial ya cerrada que empezaron entre esos momentos y cuya crónica
    // no está terminada de leer.
    Task<IReadOnlyList<MatchReportSource>> GetCandidatesAsync(string provider, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchReportCheck>> GetChecksAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken = default);

    // Borra las que no han cambiado desde ese momento: la frase de la crónica no se guarda más de lo necesario.
    Task<int> RemoveMentionsUpdatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    void Add(MatchInjuryMention mention);
    void Remove(MatchInjuryMention mention);
    void AddCheck(MatchReportCheck check);
}

public sealed record MatchReportSource(Guid MatchId, string ExternalId, DateTimeOffset KickoffAt);
