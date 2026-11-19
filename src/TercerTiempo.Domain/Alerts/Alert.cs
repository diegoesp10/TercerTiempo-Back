using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Alerts;

public enum AlertKind
{
    // La licencia del club acaba sin renovarse (prueba sin contratar o renovación cancelada).
    LicenseExpiring = 1,

    // Alguien pide acceso al acta interna o al vídeo de un partido del club.
    PassRequest = 2,

    // Para el jugador: va convocado a un partido y tiene que firmar. Solo partidos: a los entrenamientos se cita a toda
    // la plantilla y no hay convocatoria.
    MatchCallUp = 3,

    // Para el jugador: la convocatoria del partido se ha publicado y no entra en ella.
    MatchNotCalledUp = 4,

    // Para el cuerpo técnico y el club: un jugador avisa de que falta a un entrenamiento. (Los partidos no llevan
    // avisos del jugador: la convocatoria la emite el cuerpo técnico y el jugador solo la firma.)
    TrainingAbsence = 6,

    // Para el jugador: le citan a entrenar (una sesión o una serie semanal), o una sesión cambia o se anula. No se firma:
    // a los entrenamientos se cita a toda la plantilla y el jugador solo avisa si falta.
    TrainingCall = 7,
}

// Aviso dentro de la app: la campanita del panel. Cada aviso tiene un solo destinatario: una cuenta de club, un jugador
// o un técnico. La clave dice qué aviso es y de qué va, para no crearlo dos veces. Como en las redes sociales, al abrir la
// campanita los nuevos quedan vistos (dejan de contar y pasan a «Anteriores»); al pulsarlo además queda leído. Se da por
// resuelto cuando deja de tener sentido (por ejemplo, si el club reactiva la renovación).
public sealed class Alert : AggregateRoot
{
    public Guid? ClubId { get; private set; }
    public Guid? PlayerId { get; private set; }
    public Guid? CoachId { get; private set; }
    public AlertKind Kind { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    // Ruta del frontal a la que lleva el aviso.
    public string? Link { get; private set; }
    // Día al que se refiere (el último con acceso, en los de caducidad; el del partido o del entrenamiento, en los suyos).
    public DateOnly? DueOn { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SeenAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsSeen => SeenAt is not null;
    public bool IsRead => ReadAt is not null;
    // Lo que cuenta la campanita y sale arriba: ni visto ni resuelto.
    public bool IsNew => !IsSeen && !IsResolved;
    public bool IsResolved => ResolvedAt is not null;

    private Alert() { }

    public static Alert Raise(Guid clubId, AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn, DateTimeOffset now) =>
        ForClub(clubId, kind, key, title, message, link, dueOn, now);

    public static Alert ForClub(Guid clubId, AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn, DateTimeOffset now)
    {
        var alert = New(kind, key, title, message, link, dueOn, now);
        alert.ClubId = Guard.NotEmpty(clubId, nameof(clubId));
        return alert;
    }

    public static Alert ForPlayer(Guid playerId, AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn, DateTimeOffset now)
    {
        var alert = New(kind, key, title, message, link, dueOn, now);
        alert.PlayerId = Guard.NotEmpty(playerId, nameof(playerId));
        return alert;
    }

    public static Alert ForCoach(Guid coachId, AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn, DateTimeOffset now)
    {
        var alert = New(kind, key, title, message, link, dueOn, now);
        alert.CoachId = Guard.NotEmpty(coachId, nameof(coachId));
        return alert;
    }

    public bool IsFor(Guid? clubId, Guid? playerId, Guid? coachId) =>
        (ClubId is { } c && c == clubId) || (PlayerId is { } p && p == playerId) || (CoachId is { } k && k == coachId);

    public void MarkSeen(DateTimeOffset now) => SeenAt ??= now;

    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
        SeenAt ??= now;
    }

    public void Resolve(DateTimeOffset now) => ResolvedAt ??= now;

    private static Alert New(AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            Kind = kind,
            Key = Guard.NotEmpty(key, "clave", 120),
            Title = Guard.NotEmpty(title, "título", 150),
            Message = Guard.NotEmpty(message, "mensaje", 500),
            Link = string.IsNullOrWhiteSpace(link) ? null : Guard.NotEmpty(link, "enlace", 200),
            DueOn = dueOn,
            CreatedAt = now,
        };
}

// La campanita de una cuenta: cuántos avisos son nuevos (sin ver y vigentes), y los nuevos más los últimos anteriores, del
// más reciente al más antiguo (solo para leer).
public sealed record AlertPage(int Unseen, IReadOnlyList<Alert> Latest);

public interface IAlertStore
{
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    // Los de una cuenta: el club, el jugador o el técnico que haya detrás (solo uno de los tres).
    Task<IReadOnlyList<Alert>> GetForAsync(Guid? clubId, Guid? playerId, Guid? coachId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Alert>> GetByKindAsync(AlertKind kind, CancellationToken cancellationToken = default);
    Task<AlertPage> GetPageForAsync(Guid? clubId, Guid? playerId, Guid? coachId, int newTake, int earlierTake, CancellationToken cancellationToken = default);

    // Sin resolver y cuya clave empieza por keyPrefix, de cualquiera de esos destinatarios (con seguimiento, para resolverlos).
    Task<IReadOnlyList<Alert>> GetOpenByKeyPrefixAsync(
        string keyPrefix, IReadOnlyCollection<Guid> playerIds, IReadOnlyCollection<Guid> coachIds, Guid? clubId, CancellationToken cancellationToken = default);

    // Marca como vistos de una vez (sin cargarlos) esos avisos de la cuenta; los de otra cuenta se ignoran.
    Task<int> MarkSeenAsync(Guid? clubId, Guid? playerId, Guid? coachId, IReadOnlyCollection<Guid> alertIds, DateTimeOffset now, CancellationToken cancellationToken = default);

    // Marca como leídos (y vistos) de una vez, sin cargarlos, los avisos sin leer de la cuenta.
    Task<int> MarkAllReadAsync(Guid? clubId, Guid? playerId, Guid? coachId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> DeleteResolvedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
    void Add(Alert alert);
}
