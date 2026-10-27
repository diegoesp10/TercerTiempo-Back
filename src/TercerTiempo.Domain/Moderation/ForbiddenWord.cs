using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Moderation;

// Por qué no se permite: ayuda al administrador a revisar la lista.
public enum ForbiddenWordKind
{
    Profanity = 1,
    Insult = 2,
    Slur = 3,
    Threat = 4,
}

// Palabra o expresión que no se puede publicar en un comentario (las notas a los árbitros). Se guarda tal como se compara
// (ver ProfanityFilter.Normalize): en minúsculas, sin tildes salvo la ñ y con un espacio entre palabras. Una palabra suelta
// cuenta también en plural; una expresión, con sus palabras seguidas.
public sealed class ForbiddenWord
{
    public const int MinLength = 2;
    public const int MaxLength = 80;

    public string Term { get; private set; } = string.Empty;
    public ForbiddenWordKind Kind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ForbiddenWord() { }

    public static ForbiddenWord Create(string term, ForbiddenWordKind kind, DateTimeOffset now)
    {
        var normalized = ProfanityFilter.Normalize(term);
        if (normalized.Length is < MinLength or > MaxLength || !normalized.Any(char.IsLetter) || normalized.Contains('*'))
            throw new DomainException(ErrorCode.ForbiddenWordInvalid, MinLength, MaxLength);
        if (!Enum.IsDefined(kind)) throw new DomainException(ErrorCode.ForbiddenWordInvalid, MinLength, MaxLength);

        return new ForbiddenWord { Term = normalized, Kind = kind, CreatedAt = now };
    }
}

public interface IForbiddenWordStore
{
    // Los términos normalizados, de caché: es lo que se mira en cada comentario.
    Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ForbiddenWord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ForbiddenWord?> GetAsync(string term, CancellationToken cancellationToken = default);
    void Add(ForbiddenWord word);
    void Remove(ForbiddenWord word);

    // Tras guardar un cambio en la lista, para que el siguiente comentario ya lo tenga en cuenta.
    void ForgetTerms();
}
