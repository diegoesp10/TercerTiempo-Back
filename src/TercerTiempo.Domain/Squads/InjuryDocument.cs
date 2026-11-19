using System.Text;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

public enum InjuryDocumentKind
{
    Imaging = 1,
    MedicalReport = 2,
    Other = 9,
}

// Una prueba o un informe de una lesión: la radiografía, la resonancia, el informe del hospital o del médico. Son datos de
// salud, muchas veces de menores: el fichero no se sirve en abierto y solo lo abren el club, el cuerpo técnico y el propio
// jugador. Va con la lesión: si la lesión se borra, se borran también sus documentos.
public sealed class InjuryDocument : AggregateRoot
{
    public const long MaxBytes = 15L * 1024 * 1024;
    public const int MaxPerInjury = 10;
    public const int FileNameMaxLength = 150;
    public const int NoteMaxLength = 200;

    public static readonly IReadOnlySet<string> ContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png", "image/webp",
    };

    public Guid InjuryId { get; private set; }
    public Guid SquadId { get; private set; }
    public InjuryDocumentKind Kind { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public Guid? UploadedByCoachId { get; private set; }
    public Guid? UploadedByClubId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private InjuryDocument() { }

    public static InjuryDocument Create(
        PlayerInjury injury, SquadEditor editor, InjuryDocumentKind kind, string fileName, string contentType, long sizeBytes,
        string storageKey, string? note, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
            throw new DomainException(ErrorCode.InjuryDocumentInvalidKind);

        if (!ContentTypes.Contains(contentType))
            throw new DomainException(ErrorCode.InjuryDocumentFormat);

        if (sizeBytes is <= 0 or > MaxBytes)
            throw new DomainException(ErrorCode.InjuryDocumentTooLarge);

        return new InjuryDocument
        {
            Id = NewId(),
            InjuryId = injury.Id,
            SquadId = injury.SquadId,
            Kind = kind,
            FileName = SafeFileName(fileName, contentType),
            ContentType = contentType.ToLowerInvariant(),
            SizeBytes = sizeBytes,
            StorageKey = Guard.NotEmpty(storageKey, nameof(storageKey), 100),
            Note = string.IsNullOrWhiteSpace(note) ? null : Guard.NotEmpty(note, "nota", NoteMaxLength),
            UploadedByCoachId = editor.CoachId,
            UploadedByClubId = editor.ClubId,
            UploadedAt = now,
        };
    }

    // El nombre lo pone el navegador del que sube: sin carpetas, sin caracteres de control ni comillas (va en la cabecera
    // de la descarga) y recortado sin perder la extensión. Si no queda nada, uno genérico por el tipo.
    public static string SafeFileName(string? fileName, string contentType)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/')[^1]);
        var clean = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c)) clean.Append(' ');
            else if (c is not ('"' or '<' or '>' or '|' or '*' or '?' or ':')) clean.Append(c);
        }
        name = string.Join(' ', clean.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');

        if (name.Length == 0)
            name = contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? "documento.pdf" : "imagen";

        if (name.Length <= FileNameMaxLength) return name;

        var extension = Path.GetExtension(name);
        if (extension.Length > 10) extension = string.Empty;
        return name[..(FileNameMaxLength - extension.Length)].TrimEnd() + extension;
    }
}
