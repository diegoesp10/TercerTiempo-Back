using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Licensing;

public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    public int MonthlyPriceCents { get; set; } = 1000;

    public string Currency { get; set; } = "EUR";

    public int TrialDays { get; set; } = 30;

    public string Provider { get; set; } = "mock";

    public int MaxMonths { get; set; } = 12;
}

public sealed record CheckoutSession(string SessionId, string Url);

// Pasarela de pago. La implementación actual es una simulación; Stripe encajará aquí (Checkout Session + webhook).
public interface ILicensePaymentGateway
{
    string Provider { get; }
    Task<CheckoutSession> BeginCheckoutAsync(Guid clubId, Guid teamId, int months, int amountCents, string currency, CancellationToken cancellationToken);
    Task<bool> ConfirmAsync(string sessionId, CancellationToken cancellationToken);
}

// Puerta de licencias: decide si una plantilla, un vídeo o el panel de un jugador están accesibles según la licencia
// del equipo (y la del espectador cuando mira datos de otro equipo). No sustituye a los permisos de cada acción.
public interface ILicenseGate
{
    Task<TeamLicense> EnsureForTeamAsync(Club club, Guid teamId, CancellationToken cancellationToken);
    Task<LicenseStatus> StatusForTeamAsync(Guid teamId, CancellationToken cancellationToken);
    Task<bool> IsViewerLicensedAsync(ICurrentUser user, CancellationToken cancellationToken);
    Task<bool> CanAccessSquadAsync(Squad squad, ICurrentUser user, CancellationToken cancellationToken);
    Task EnsureSquadAccessAsync(Guid squadId, ICurrentUser user, CancellationToken cancellationToken);
    Task EnsureVideoAccessAsync(Guid videoId, ICurrentUser user, CancellationToken cancellationToken);
    Task EnsureSharedVideoAccessAsync(string token, CancellationToken cancellationToken);
    Task EnsurePlayerAccessAsync(ICurrentUser user, CancellationToken cancellationToken);
}
