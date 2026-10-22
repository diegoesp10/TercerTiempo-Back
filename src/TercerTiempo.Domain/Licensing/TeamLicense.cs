using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Licensing;

public enum LicenseStatus
{
    Trial = 1,
    Active = 2,
    Expired = 3,
}

// Licencia de uso por equipo: la compra el club que gestiona el equipo. Empieza con un periodo de prueba y después
// se paga por meses. Sin licencia vigente, el club, el cuerpo técnico y los jugadores del equipo pierden el acceso a
// sus datos privados (que se conservan), no a los datos públicos de la federación.
public sealed class TeamLicense : AggregateRoot
{
    public Guid ClubId { get; private set; }
    public Guid TeamId { get; private set; }
    public DateOnly TrialEndsOn { get; private set; }
    public DateOnly? PaidUntil { get; private set; }
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
        : LicenseStatus.Expired;

    public bool IsActiveOn(DateOnly today) => StatusOn(today) != LicenseStatus.Expired;

    public DateOnly ValidUntil => PaidUntil is { } paid && paid > TrialEndsOn ? paid : TrialEndsOn;

    // Un pago añade meses a partir de la fecha más lejana entre hoy, el fin de la prueba y lo ya pagado: pagar durante
    // la prueba no quita días de prueba.
    public void ExtendPaid(int months, DateOnly today, DateTimeOffset now)
    {
        Guard.InRange(months, 1, 24, "meses");
        var from = ValidUntil > today ? ValidUntil : today;
        PaidUntil = from.AddMonths(months);
        UpdatedAt = now;
    }
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Cancelled = 3,
}

// Intento de pago de una licencia a través de la pasarela (de momento simulada). Es el rastro que luego enlazará con
// la sesión de Stripe y su webhook.
public sealed class LicensePayment : AggregateRoot
{
    public Guid LicenseId { get; private set; }
    public Guid ClubId { get; private set; }
    public Guid TeamId { get; private set; }
    public int Months { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string Provider { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    private LicensePayment() { }

    public static LicensePayment Begin(TeamLicense license, int months, int monthlyPriceCents, string currency, string provider, string sessionId, DateTimeOffset now)
    {
        Guard.InRange(months, 1, 24, "meses");
        Guard.NotNegative(monthlyPriceCents, "precio");
        return new LicensePayment
        {
            Id = NewId(),
            LicenseId = license.Id,
            ClubId = license.ClubId,
            TeamId = license.TeamId,
            Months = months,
            AmountCents = months * monthlyPriceCents,
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
    Task<IReadOnlyList<TeamLicense>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    void Add(TeamLicense license);

    Task<LicensePayment?> GetPaymentBySessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LicensePayment>> GetPaymentsByClubAsync(Guid clubId, CancellationToken cancellationToken = default);
    void AddPayment(LicensePayment payment);
}
