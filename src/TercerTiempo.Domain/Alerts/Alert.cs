using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Alerts;

public enum AlertKind
{
    // La licencia del club acaba sin renovarse (prueba sin contratar o renovación cancelada).
    LicenseExpiring = 1,
}

// Aviso para un club dentro de la app: la campanita de su panel. Solo los reciben las cuentas de club (ni el cuerpo
// técnico ni los jugadores). La clave dice qué aviso es y de qué fecha, para no crearlo dos veces; se marca como leído
// al verlo y se da por resuelto cuando deja de tener sentido (por ejemplo, si el club reactiva la renovación).
public sealed class Alert : AggregateRoot
{
    public Guid ClubId { get; private set; }
    public AlertKind Kind { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    // Ruta del frontal a la que lleva el aviso.
    public string? Link { get; private set; }
    // Día al que se refiere (el último con acceso, en los de caducidad).
    public DateOnly? DueOn { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsRead => ReadAt is not null;
    public bool IsResolved => ResolvedAt is not null;

    private Alert() { }

    public static Alert Raise(Guid clubId, AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            Kind = kind,
            Key = Guard.NotEmpty(key, "clave", 120),
            Title = Guard.NotEmpty(title, "título", 150),
            Message = Guard.NotEmpty(message, "mensaje", 500),
            Link = string.IsNullOrWhiteSpace(link) ? null : Guard.NotEmpty(link, "enlace", 200),
            DueOn = dueOn,
            CreatedAt = now,
        };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    public void Resolve(DateTimeOffset now) => ResolvedAt ??= now;
}

public interface IAlertStore
{
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> GetByKindAsync(AlertKind kind, CancellationToken cancellationToken = default);
    void Add(Alert alert);
}
