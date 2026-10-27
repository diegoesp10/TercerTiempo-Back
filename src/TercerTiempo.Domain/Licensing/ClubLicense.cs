using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Licensing;

public enum LicenseStatus
{
    Trial = 1,
    Active = 2,
    Expired = 3,
}

// Lo que se cobra en una suscripción: la licencia de club y, aparte, cada licencia de técnico (los precios están en la
// configuración).
public enum LicenseItem
{
    Club = 1,
    StaffSeat = 2,
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

// Licencia de uso de un club: una por club, cubre el primer equipo y todas sus filiales (los equipos que tiene
// vinculados) y da todas las funciones al club, que hace lo mismo que su cuerpo técnico. Los técnicos con cuenta van
// aparte: una bolsa de licencias de técnico del club, que las reparte entre sus equipos como quiera y que se amplía o se
// reduce una vez contratada la licencia de club. Empieza con un periodo de prueba (con unos técnicos incluidos) y después
// se paga como suscripción mensual o anual que se renueva sola hasta que el club la cancela. Sin licencia vigente, el
// club, el cuerpo técnico y los jugadores de sus equipos pierden el acceso a los datos privados (que se conservan), no a
// los datos públicos de la federación.
public sealed class ClubLicense : AggregateRoot
{
    // Días de cortesía tras el fin de lo pagado mientras se cobra la renovación (la pasarela reintenta los cobros
    // fallidos durante unos días antes de dar la suscripción por perdida).
    public const int RenewalGraceDays = 3;

    public Guid ClubId { get; private set; }
    public DateOnly TrialEndsOn { get; private set; }
    public DateOnly? PaidUntil { get; private set; }

    // Suscripción contratada: null mientras el club no ha contratado la licencia.
    public BillingInterval? Interval { get; private set; }
    public bool AutoRenew { get; private set; }

    // Licencias de técnico contratadas: la bolsa del club para todos sus equipos.
    public int StaffSeats { get; private set; }

    // Cambios pedidos para la próxima renovación: facturación y reducción de técnicos.
    public BillingInterval? NextInterval { get; private set; }
    public int? NextStaffSeats { get; private set; }

    // Referencias en la pasarela (cliente y suscripción de Stripe, o las simuladas).
    public string? Provider { get; private set; }
    public string? CustomerRef { get; private set; }
    public string? SubscriptionRef { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ClubLicense() { }

    public static ClubLicense Start(Guid clubId, DateOnly startedOn, int trialDays, DateTimeOffset now)
    {
        Guard.InRange(trialDays, 0, 365, "días de prueba");
        return new ClubLicense
        {
            Id = NewId(),
            ClubId = Guard.NotEmpty(clubId, nameof(clubId)),
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

    public bool IsContracted => Interval is not null;

    public bool IsRenewing => AutoRenew && Interval is not null;

    // Día del próximo cobro de la suscripción: el siguiente al último con acceso.
    public DateOnly? RenewsOn => IsRenewing ? ValidUntil.AddDays(1) : null;

    // Técnicos que entran en la próxima renovación.
    public int RenewalStaffSeats => NextStaffSeats ?? StaffSeats;

    // Técnicos que admite ahora la licencia: los de la prueba mientras no se ha contratado; después, los contratados o,
    // si hay una reducción pedida, los de la renovación, para no llegar a ella con más técnicos de los que se pagan.
    public int StaffLimit(int trialStaffSeats) =>
        !IsContracted ? trialStaffSeats
        : NextStaffSeats is { } next && next < StaffSeats ? next : StaffSeats;

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

    // El club ha completado el alta de la suscripción (la licencia de club y los técnicos que eligió). Lo pagado se
    // extiende cuando la pasarela confirma el cobro (RecordPaidPeriod); si se contrata durante la prueba no se cobra nada
    // hasta que acaba.
    public void Subscribe(BillingInterval interval, int staffSeats, string provider, string? subscriptionRef, string? customerRef, DateTimeOffset now)
    {
        if (!Enum.IsDefined(interval)) throw new DomainException(ErrorCode.LicensePlanInvalid);

        Interval = interval;
        StaffSeats = Guard.NotNegative(staffSeats, "técnicos");
        AutoRenew = true;
        NextInterval = null;
        NextStaffSeats = null;
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

    // Cobro de una renovación: aplica los cambios pedidos y extiende lo pagado.
    public void RecordRenewal(DateOnly periodEnd, DateTimeOffset now)
    {
        if (NextInterval is { } interval) Interval = interval;
        if (NextStaffSeats is { } seats) StaffSeats = seats;
        NextInterval = null;
        NextStaffSeats = null;
        RecordPaidPeriod(periodEnd, now);
    }

    // Facturación de la próxima renovación. Si ahora mismo no hay nada cobrado (la prueba), se cambia ya.
    public void ChangeRenewal(BillingInterval interval, DateOnly today, DateTimeOffset now)
    {
        if (!IsRenewing || !IsActiveOn(today)) throw new DomainException(ErrorCode.LicenseNoSubscription);
        if (!Enum.IsDefined(interval)) throw new DomainException(ErrorCode.LicensePlanInvalid);

        if (!HasPaidPeriod(today))
        {
            Interval = interval;
            NextInterval = null;
        }
        else
        {
            NextInterval = interval == Interval ? null : interval;
        }
        UpdatedAt = now;
    }

    // Licencias de técnico, solo con la licencia de club contratada. Añadir vale ya (devuelve cuántas se añaden, para
    // cobrar su parte proporcional); quitar vale ya durante la prueba y, con un periodo pagado, desde la próxima renovación.
    public int ChangeStaffSeats(int staffSeats, DateOnly today, DateTimeOffset now)
    {
        if (!IsContracted) throw new DomainException(ErrorCode.LicenseClubRequired);
        if (!IsRenewing || !IsActiveOn(today)) throw new DomainException(ErrorCode.LicenseNoSubscription);
        Guard.NotNegative(staffSeats, "técnicos");

        var added = Math.Max(0, staffSeats - StaffSeats);
        if (added > 0 || !HasPaidPeriod(today))
        {
            StaffSeats = staffSeats;
            NextStaffSeats = null;
        }
        else
        {
            NextStaffSeats = staffSeats == StaffSeats ? null : staffSeats;
        }
        UpdatedAt = now;
        return added;
    }

    // Parte proporcional de los técnicos añadidos hasta el final del periodo pagado, contando hoy. Durante la prueba no
    // se cobra nada: entran en el primer cobro.
    public int StaffSeatsProrationCents(int added, int seatPeriodPriceCents, DateOnly today)
    {
        if (added <= 0 || !HasPaidPeriod(today) || Interval is not { } interval) return 0;

        var paid = PaidUntil!.Value;
        var periodDays = paid.DayNumber - paid.AddMonths(-interval.Months()).DayNumber;
        var remaining = Math.Min(paid.DayNumber - today.DayNumber + 1, periodDays);
        return (int)Math.Round((decimal)added * seatPeriodPriceCents * remaining / periodDays, MidpointRounding.AwayFromZero);
    }

    // Deja de renovarse: el acceso sigue hasta el final de lo pagado (o de la prueba).
    public void CancelRenewal(DateTimeOffset now)
    {
        if (!IsRenewing) throw new DomainException(ErrorCode.LicenseNoSubscription);
        AutoRenew = false;
        NextInterval = null;
        NextStaffSeats = null;
        UpdatedAt = now;
    }

    public void ResumeRenewal(DateOnly today, DateTimeOffset now)
    {
        if (!IsContracted || AutoRenew || !IsActiveOn(today)) throw new DomainException(ErrorCode.LicenseNoSubscription);
        AutoRenew = true;
        UpdatedAt = now;
    }

    // La pasarela da la suscripción por terminada (cancelada o con los cobros agotados).
    public void EndSubscription(DateTimeOffset now)
    {
        AutoRenew = false;
        NextInterval = null;
        NextStaffSeats = null;
        UpdatedAt = now;
    }

    private bool HasPaidPeriod(DateOnly today) => PaidUntil is { } paid && paid >= today;
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Cancelled = 3,
    // Cobro rechazado (tarjeta denegada, sin fondos...): la pasarela lo reintenta y, si se agotan los intentos, da la
    // suscripción por terminada.
    Failed = 4,
}

public enum LicensePaymentKind
{
    // Alta de la suscripción (pago en la pasarela).
    Subscription = 1,
    // Cobro automático de un periodo nuevo.
    Renewal = 2,
    // Técnicos añadidos a mitad de periodo: su parte proporcional hasta la renovación.
    StaffSeats = 3,
}

// Pago de una licencia a través de la pasarela: el alta de una suscripción (sesión de Checkout), cada renovación
// (factura) o la ampliación de técnicos. Es el rastro que enlaza con Stripe y su webhook.
public sealed class LicensePayment : AggregateRoot
{
    public Guid LicenseId { get; private set; }
    public Guid ClubId { get; private set; }
    public LicensePaymentKind Kind { get; private set; }
    public BillingInterval Interval { get; private set; }
    // Licencias de técnico de la suscripción con este pago.
    public int StaffSeats { get; private set; }
    public int Months { get; private set; }
    // Lo que se cobra en este pago (0 si se contrata durante la prueba: el primer cobro llega al acabarla).
    public int AmountCents { get; private set; }
    // Precio de cada periodo de la suscripción (licencia de club más técnicos).
    public int PeriodPriceCents { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string Provider { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    private LicensePayment() { }

    public static LicensePayment Begin(
        ClubLicense license, LicensePaymentKind kind, BillingInterval interval, int staffSeats, int amountCents, int periodPriceCents,
        string currency, string provider, string sessionId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(interval)) throw new DomainException(ErrorCode.LicensePlanInvalid);
        Guard.NotNegative(staffSeats, "técnicos");
        Guard.NotNegative(amountCents, "importe");
        Guard.NotNegative(periodPriceCents, "precio");
        return new LicensePayment
        {
            Id = NewId(),
            LicenseId = license.Id,
            ClubId = license.ClubId,
            Kind = kind,
            Interval = interval,
            StaffSeats = staffSeats,
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

    // Motivo del rechazo de un cobro fallido, tal como lo da la pasarela.
    public string? FailureReason { get; private set; }

    public bool IsPaid => Status == PaymentStatus.Paid;

    public void MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Paid)
            throw new DomainException(ErrorCode.LicensePaymentAlreadyPaid);
        Status = PaymentStatus.Failed;
        FailureReason = Guard.NotEmpty(reason, "motivo", 300);
    }

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
    Task<ClubLicense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClubLicense?> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<ClubLicense?> GetBySubscriptionAsync(string subscriptionRef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubLicense>> GetRenewingAsync(string provider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubLicense>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(ClubLicense license);

    Task<LicensePayment?> GetPaymentBySessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LicensePayment>> GetPaymentsByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LicensePayment>> GetAllPaymentsAsync(CancellationToken cancellationToken = default);
    void AddPayment(LicensePayment payment);
}
