using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Licensing;

public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    public string Currency { get; set; } = "EUR";

    public int TrialDays { get; set; } = 30;

    // «mock» (pago simulado, sin cobro real) o «stripe».
    public string Provider { get; set; } = "mock";

    public LicensePlanOptions Basic { get; set; } = new() { MonthlyPriceCents = 990, YearlyPriceCents = 9900, MaxStaff = 1 };

    public LicensePlanOptions Premium { get; set; } = new() { MonthlyPriceCents = 1990, YearlyPriceCents = 19900, MaxStaff = 6 };

    public StripeOptions Stripe { get; set; } = new();

    public LicensePlanOptions For(LicensePlan plan) => plan == LicensePlan.Premium ? Premium : Basic;

    public int PriceCents(LicensePlan plan, BillingInterval interval) =>
        interval == BillingInterval.Yearly ? For(plan).YearlyPriceCents : For(plan).MonthlyPriceCents;
}

public sealed class LicensePlanOptions
{
    public int MonthlyPriceCents { get; set; }

    public int YearlyPriceCents { get; set; }

    // Técnicos (nombramientos vigentes o pendientes de canjear) que admite el equipo en la temporada.
    public int MaxStaff { get; set; }

    // Precios recurrentes de Stripe (price_...) de cada periodo.
    public string StripeMonthlyPriceId { get; set; } = string.Empty;

    public string StripeYearlyPriceId { get; set; } = string.Empty;

    public string StripePriceId(BillingInterval interval) => interval == BillingInterval.Yearly ? StripeYearlyPriceId : StripeMonthlyPriceId;
}

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;

    // Secreto de firma del endpoint de webhooks (whsec_...).
    public string WebhookSecret { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.stripe.com/";

    // Versión fija de la API (vacío = la de la cuenta).
    public string ApiVersion { get; set; } = string.Empty;

    // Vuelta al frontal tras pagar; Stripe sustituye {CHECKOUT_SESSION_ID}.
    public string SuccessUrl { get; set; } = "http://localhost:5173/pago/{CHECKOUT_SESSION_ID}";

    public string CancelUrl { get; set; } = "http://localhost:5173/licencias";

    // Segundos de tolerancia del sello de tiempo de la firma del webhook.
    public int WebhookToleranceSeconds { get; set; } = 300;
}

public sealed record CheckoutRequest(
    Guid ClubId,
    Guid TeamId,
    Guid LicenseId,
    string TeamName,
    LicensePlan Plan,
    BillingInterval Interval,
    int PeriodPriceCents,
    string Currency,
    // Último día ya cubierto (prueba o periodo pagado): el primer cobro es al día siguiente. null = se cobra ya.
    DateOnly? DeferUntil,
    string? CustomerRef);

public sealed record CheckoutSession(string SessionId, string Url);

// Estado de una sesión de pago en la pasarela. PeriodEnd: último día pagado si ya se ha cobrado algo.
public sealed record CheckoutResult(bool Completed, string? SubscriptionRef, string? CustomerRef, DateOnly? PeriodEnd);

// Avisos de la pasarela (webhooks) que cambian una licencia.
public abstract record LicenseWebhookEvent;

public sealed record CheckoutCompletedEvent(string SessionId) : LicenseWebhookEvent;

// Factura cobrada de una suscripción: alta (BillingReason «subscription_create») o renovación.
public sealed record InvoicePaidEvent(
    string InvoiceId,
    string SubscriptionRef,
    Guid? LicenseId,
    string BillingReason,
    int AmountCents,
    string Currency,
    DateOnly PeriodEnd) : LicenseWebhookEvent;

public sealed record SubscriptionEndedEvent(string SubscriptionRef) : LicenseWebhookEvent;

// Pasarela de pago de las licencias: suscripciones mensuales o anuales. La simulada confirma todo con un clic y cobra
// las renovaciones con un trabajo en segundo plano; la de Stripe usa Checkout en modo suscripción y sus webhooks.
public interface ILicensePaymentGateway
{
    string Provider { get; }

    // true si la pasarela cobra sola las renovaciones y avisa por webhook; false si las cobra el trabajo de renovaciones.
    bool RenewsByWebhook { get; }

    Task<CheckoutSession> BeginCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    Task<CheckoutResult> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken);

    Task CancelRenewalAsync(string subscriptionRef, CancellationToken cancellationToken);

    Task ResumeRenewalAsync(string subscriptionRef, CancellationToken cancellationToken);

    // Cambia el precio de la suscripción a partir de la próxima renovación (sin prorratear).
    Task ChangeRenewalAsync(string subscriptionRef, LicensePlan plan, BillingInterval interval, CancellationToken cancellationToken);

    // Valida la firma y traduce el aviso; null si no es un evento que importe. Lanza si la firma no es válida.
    LicenseWebhookEvent? ParseWebhook(string payload, string? signature);
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

    // Sin licencia vigente nadie del club (club, cuerpo técnico ni jugadores) puede trabajar con los datos de ese equipo.
    Task EnsureTeamLicensedAsync(Guid teamId, CancellationToken cancellationToken);
}
