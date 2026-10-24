using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Licensing;

public enum LicenseStatus
{
    Trial = 1,
    Active = 2,
    Expired = 3,
}

// Planes de la licencia. Los dos dan las mismas funciones a la plantilla entera; cambia el tamaño del cuerpo técnico
// que puede tener el equipo (los límites y precios están en la configuración).
public enum LicensePlan
{
    Basic = 1,
    Premium = 2,
}

public enum BillingInterval
{
    Monthly = 1,
    Yearly = 2,
}

public static class BillingIntervals
{
    public static int Months(this BillingInterval interval) => interval == BillingInterval.Yearly ? 12 : 1;
}

// Licencia de uso por equipo: la compra el club que gestiona el equipo. Empieza con un periodo de prueba (con todo lo
// de Premium) y después se paga como suscripción mensual o anual que se renueva sola hasta que el club la cancela.
// Sin licencia vigente, el club, el cuerpo técnico y los jugadores del equipo pierden el acceso a sus datos privados
// (que se conservan), no a los datos públicos de la federación.
public sealed class TeamLicense : AggregateRoot
{
    // Días de cortesía tras el fin de lo pagado mientras se cobra la renovación (la pasarela reintenta los cobros
    // fallidos durante unos días antes de dar la suscripción por perdida).
    public const int RenewalGraceDays = 3;

    public Guid ClubId { get; private set; }
    public Guid TeamId { get; private set; }
    public DateOnly TrialEndsOn { get; private set; }
    public DateOnly? PaidUntil { get; private set; }

    // Suscripción contratada: null mientras el club no ha elegido plan.
    public LicensePlan? Plan { get; private set; }
    public BillingInterval? Interval { get; private set; }
    public bool AutoRenew { get; private set; }

    // Cambio de plan o de facturación pedido para la próxima renovación.
    public LicensePlan? NextPlan { get; private set; }
    public BillingInterval? NextInterval { get; private set; }

    // Referencias en la pasarela (cliente y suscripción de Stripe, o las simuladas).
    public string? Provider { get; private set; }
    public string? CustomerRef { get; private set; }
    public string? SubscriptionRef { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TeamLicense() { }

    public static TeamLicense Start(Guid clubId, Guid teamId, DateOnly startedOn, int trialDays, DateTimeOffset now)
    {
        Guard.InRange(trialDays, 0, 365, "días de prueba");
        return new TeamLicense
        {
            Id = NewId(),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            TrialEndsOn = startedOn.AddDays(trialDays),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public LicenseStatus StatusOn(DateOnly today) =>
        PaidUntil is { } paid && paid >= today ? LicenseStatus.Active
        : TrialEndsOn >= today ? LicenseStatus.Trial
        : IsRenewing && ValidUntil.AddDays(RenewalGraceDays) >= today ? LicenseStatus.Active
        : LicenseStatus.Expired;

    public bool IsActiveOn(DateOnly today) => StatusOn(today) != LicenseStatus.Expired;

    public DateOnly ValidUntil => PaidUntil is { } paid && paid > TrialEndsOn ? paid : TrialEndsOn;

    public bool IsRenewing => AutoRenew && Plan is not null;

    // Día del próximo cobro de la suscripción: el siguiente al último con acceso.
    public DateOnly? RenewsOn => IsRenewing ? ValidUntil.AddDays(1) : null;

    // Plan con el que funciona el equipo: el contratado o, mientras no hay ninguno (la prueba), Premium.
    public LicensePlan EffectivePlan => Plan ?? LicensePlan.Premium;

    // Plan que limita el cuerpo técnico: si hay un cambio pedido, el más pequeño de los dos, para no llegar a la
    // renovación con más técnicos de los que admite el plan siguiente.
    public LicensePlan StaffPlan => NextPlan is { } next && next < EffectivePlan ? next : EffectivePlan;

    // Hasta dónde llega un periodo nuevo que empieza al acabar lo que ya hay (o hoy, si ha caducado): pagar durante
    // la prueba o antes de que acabe el mes no quita días.
    public DateOnly NextPeriodEnd(BillingInterval interval, DateOnly today)
    {
        var from = ValidUntil > today ? ValidUntil : today;
        return from.AddMonths(interval.Months());
    }

    // Final del periodo que cobra una renovación: sigue al anterior sin huecos, salvo que lleve caducada más de los
    // días de cortesía (entonces empieza hoy).
    public DateOnly RenewalPeriodEnd(BillingInterval interval, DateOnly today)
    {
        var from = ValidUntil.AddDays(RenewalGraceDays) >= today ? ValidUntil : today;
        return from.AddMonths(interval.Months());
    }

    // El club ha completado el alta de una suscripción. Lo pagado se extiende cuando la pasarela confirma el cobro
    // (RecordPaidPeriod); si se contrata durante la prueba no se cobra nada hasta que acaba.
    public void Subscribe(LicensePlan plan, BillingInterval interval, string provider, string? subscriptionRef, string? customerRef, DateTimeOffset now)
    {
        if (!Enum.IsDefined(plan) || !Enum.IsDefined(interval)) throw new DomainException(ErrorCode.LicensePlanInvalid);

        Plan = plan;
        Interval = interval;
        AutoRenew = true;
        NextPlan = null;
        NextInterval = null;
        Provider = Guard.NotEmpty(provider, "pasarela", 40);
        SubscriptionRef = subscriptionRef ?? SubscriptionRef;
        CustomerRef = customerRef ?? CustomerRef;
        UpdatedAt = now;
    }

    // Cobro confirmado de un periodo: extiende lo pagado hasta su final (nunca lo acorta, así que los avisos
    // repetidos de la pasarela no cambian nada).
    public void RecordPaidPeriod(DateOnly periodEnd, DateTimeOffset now)
    {
        if (PaidUntil is null || periodEnd > PaidUntil) PaidUntil = periodEnd;
        UpdatedAt = now;
    }

    // Cobro de una renovación: aplica el cambio de plan pedido y extiende lo pagado.
    public void RecordRenewal(DateOnly periodEnd, DateTimeOffset now)
    {
        if (NextPlan is { } plan) Plan = plan;
        if (NextInterval is { } interval) Interval = interval;
        NextPlan = null;
        NextInterval = null;
        RecordPaidPeriod(periodEnd, now);
    }

    // Plan para la próxima renovación. Si ahora mismo no hay nada cobrado (la prueba), se cambia ya.
    public void ChangeRenewal(LicensePlan plan, BillingInterval interval, DateOnly today, DateTimeOffset now)
    {
        if (!IsRenewing || !IsActiveOn(today)) throw new DomainException(ErrorCode.LicenseNoSubscription);
        if (!Enum.IsDefined(plan) || !Enum.IsDefined(interval)) throw new DomainException(ErrorCode.LicensePlanInvalid);

        if (PaidUntil is null || PaidUntil < today)
        {
            Plan = plan;
            Interval = interval;
            NextPlan = null;
            NextInterval = null;
        }
        else
        {
            var same = plan == Plan && interval == Interval;
            NextPlan = same ? null : plan;
            NextInterval = same ? null : interval;
        }
        UpdatedAt = now;
    }

    // Deja de renovarse: el acceso sigue hasta el final de lo pagado (o de la prueba).
    public void CancelRenewal(DateTimeOffset now)
    {
        if (!IsRenewing) throw new DomainException(ErrorCode.LicenseNoSubscription);
        AutoRenew = false;
        NextPlan = null;
        NextInterval = null;
        UpdatedAt = now;
    }

    public void ResumeRenewal(DateOnly today, DateTimeOffset now)
    {
        if (Plan is null || AutoRenew || !IsActiveOn(today)) throw new DomainException(ErrorCode.LicenseNoSubscription);
        AutoRenew = true;
        UpdatedAt = now;
    }

    // La pasarela da la suscripción por terminada (cancelada o con los cobros agotados).
    public void EndSubscription(DateTimeOffset now)
    {
        AutoRenew = false;
        NextPlan = null;
        NextInterval = null;
        UpdatedAt = now;
    }
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Cancelled = 3,
}

public enum LicensePaymentKind
{
    // Alta de la suscripción (pago en la pasarela).
    Subscription = 1,
    // Cobro automático de un periodo nuevo.
    Renewal = 2,
}

// Pago de una licencia a través de la pasarela: el alta de una suscripción (sesión de Checkout) o cada renovación
// (factura). Es el rastro que enlaza con Stripe y su webhook.
public sealed class LicensePayment : AggregateRoot
{
    public Guid LicenseId { get; private set; }
    public Guid ClubId { get; private set; }
    public Guid TeamId { get; private set; }
    public LicensePaymentKind Kind { get; private set; }
    public LicensePlan Plan { get; private set; }
    public BillingInterval Interval { get; private set; }
    public int Months { get; private set; }
    // Lo que se cobra en este pago (0 si se contrata durante la prueba: el primer cobro llega al acabarla).
    public int AmountCents { get; private set; }
    // Precio de cada periodo de la suscripción.
    public int PeriodPriceCents { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string Provider { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    private LicensePayment() { }

    public static LicensePayment Begin(
        TeamLicense license, LicensePaymentKind kind, LicensePlan plan, BillingInterval interval, int amountCents, int periodPriceCents,
        string currency, string provider, string sessionId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(plan) || !Enum.IsDefined(interval)) throw new DomainException(ErrorCode.LicensePlanInvalid);
        Guard.NotNegative(amountCents, "importe");
        Guard.NotNegative(periodPriceCents, "precio");
        return new LicensePayment
        {
            Id = NewId(),
            LicenseId = license.Id,
            ClubId = license.ClubId,
            TeamId = license.TeamId,
            Kind = kind,
            Plan = plan,
            Interval = interval,
            Months = interval.Months(),
            AmountCents = amountCents,
            PeriodPriceCents = periodPriceCents,
            Currency = Guard.NotEmpty(currency, "moneda", 3),
            Provider = Guard.NotEmpty(provider, "pasarela", 40),
            SessionId = Guard.NotEmpty(sessionId, "sesión", 120),
            Status = PaymentStatus.Pending,
            CreatedAt = now,
        };
    }

    public bool IsPaid => Status == PaymentStatus.Paid;

    public void MarkPaid(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Paid)
            throw new DomainException(ErrorCode.LicensePaymentAlreadyPaid);
        if (Status == PaymentStatus.Cancelled)
            throw new DomainException(ErrorCode.LicensePaymentCancelled);
        Status = PaymentStatus.Paid;
        PaidAt = now;
    }

    public void Cancel()
    {
        if (Status == PaymentStatus.Paid)
            throw new DomainException(ErrorCode.LicensePaymentAlreadyPaid);
        Status = PaymentStatus.Cancelled;
    }
}

public interface ILicenseStore
{
    Task<TeamLicense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TeamLicense?> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<TeamLicense?> GetBySubscriptionAsync(string subscriptionRef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamLicense>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamLicense>> GetRenewingAsync(string provider, CancellationToken cancellationToken = default);
    void Add(TeamLicense license);

    Task<LicensePayment?> GetPaymentBySessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LicensePayment>> GetPaymentsByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    void AddPayment(LicensePayment payment);
}
