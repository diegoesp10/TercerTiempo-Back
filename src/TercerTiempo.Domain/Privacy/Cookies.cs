using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Privacy;

// Necesarias (sesión, elección de cookies) y de preferencias que elige el propio usuario (tema, temporada) están exentas
// de consentimiento; las de analítica solo se usan si se aceptan.
public enum CookieCategory
{
    Necessary = 1,
    Preferences = 2,
    Analytics = 3,
}

public enum CookieStorage
{
    Cookie = 1,
    LocalStorage = 2,
    SessionStorage = 3,
}

// Una cookie o dato guardado en el navegador, tal y como se explica en la política de cookies y en el panel de ajustes.
public sealed class CookieDefinition : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public CookieCategory Category { get; private set; }
    public CookieStorage Storage { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public string Duration { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    private CookieDefinition() { }

    public static CookieDefinition Create(string name, CookieCategory category, CookieStorage storage, string provider, string purpose, string duration, int sortOrder)
    {
        var cookie = new CookieDefinition { Id = NewId(), Name = Guard.NotEmpty(name, "nombre", 100) };
        cookie.Update(category, storage, provider, purpose, duration, sortOrder);
        return cookie;
    }

    public bool RequiresConsent => Category == CookieCategory.Analytics;

    public void Update(CookieCategory category, CookieStorage storage, string provider, string purpose, string duration, int sortOrder)
    {
        if (!Enum.IsDefined(category) || !Enum.IsDefined(storage))
            throw new DomainException(ErrorCode.CookieInvalid);

        Category = category;
        Storage = storage;
        Provider = Guard.NotEmpty(provider, "responsable", 100);
        Purpose = Guard.NotEmpty(purpose, "finalidad", 500);
        Duration = Guard.NotEmpty(duration, "duración", 100);
        SortOrder = Guard.InRange(sortOrder, 0, 1000, "orden");
    }
}

// Versión vigente de la política de cookies (una fila). Al cambiarla, el aviso vuelve a salir a todos para que elijan
// de nuevo: un consentimiento solo vale para la versión con la que se dio.
public sealed class CookiePolicy : AggregateRoot
{
    public static readonly Guid SingletonId = new("01a16f00-0000-7000-8000-000000000003");
    public const string InitialVersion = "2026-10-24";

    public string Version { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private CookiePolicy() { }

    public static CookiePolicy Create(string version, DateTimeOffset now) =>
        new() { Id = SingletonId, Version = Guard.NotEmpty(version, "versión", 40), UpdatedAt = now };

    public void Publish(string version, DateTimeOffset now)
    {
        Version = Guard.NotEmpty(version, "versión", 40);
        UpdatedAt = now;
    }
}

public enum ConsentAction
{
    AcceptAll = 1,
    RejectAll = 2,
    Custom = 3,
}

// Cada elección de cookies queda guardada (también los cambios), para poder demostrar el consentimiento: el visitante
// anónimo (un id aleatorio de su navegador), su cuenta si había iniciado sesión, qué aceptó y con qué versión. Ni IP
// ni navegador.
public sealed class CookieConsent : AggregateRoot
{
    public const int VisitorIdLength = 36;

    public string VisitorId { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public bool Analytics { get; private set; }
    public ConsentAction Action { get; private set; }
    public string PolicyVersion { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private CookieConsent() { }

    public static CookieConsent Record(string visitorId, Guid? userId, bool analytics, ConsentAction action, string policyVersion, DateTimeOffset now)
    {
        if (!Guid.TryParse(visitorId, out _))
            throw new DomainException(ErrorCode.CookieVisitorInvalid);
        if (!Enum.IsDefined(action))
            throw new DomainException(ErrorCode.CookieInvalid);

        return new CookieConsent
        {
            Id = NewId(),
            VisitorId = visitorId.ToLowerInvariant(),
            UserId = userId,
            Analytics = action switch { ConsentAction.AcceptAll => true, ConsentAction.RejectAll => false, _ => analytics },
            Action = action,
            PolicyVersion = policyVersion,
            CreatedAt = now,
        };
    }
}

public interface ICookieStore
{
    Task<IReadOnlyList<CookieDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CookieDefinition>> GetAllForUpdateAsync(CancellationToken cancellationToken = default);
    void Add(CookieDefinition cookie);
    void Remove(CookieDefinition cookie);

    Task<CookiePolicy?> GetPolicyAsync(CancellationToken cancellationToken = default);
    Task<CookiePolicy?> GetPolicyForUpdateAsync(CancellationToken cancellationToken = default);
    void AddPolicy(CookiePolicy policy);

    void AddConsent(CookieConsent consent);
    Task<CookieConsent?> GetLatestConsentAsync(string visitorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CookieConsent>> GetConsentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
