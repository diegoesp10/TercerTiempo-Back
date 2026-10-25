using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Admin;

// Estado de cobro de una licencia para el administrador:
//   trial        en prueba sin plan elegido;
//   trial-plan   en prueba con plan elegido (el primer cobro llega al acabarla);
//   active       pagada y al día;
//   grace        se pasó la fecha de renovación y el cobro está pendiente (días de cortesía);
//   unpaid       caducada con la renovación activa: no se ha podido cobrar;
//   cancelled    caducada tras cancelar la renovación (no debe nada);
//   trial-ended  acabó la prueba sin contratar.
public static class LicenseBillingState
{
    public const string Trial = "trial";
    public const string TrialWithPlan = "trial-plan";
    public const string Active = "active";
    public const string Grace = "grace";
    public const string Unpaid = "unpaid";
    public const string Cancelled = "cancelled";
    public const string TrialEnded = "trial-ended";
}

public sealed record LicensesOverviewDto(
    LicensesSummaryDto Summary,
    IReadOnlyList<AdminLicenseDto> Licenses,
    IReadOnlyList<AdminLicensePaymentDto> Payments,
    string Currency,
    DateOnly Today,
    [property: System.ComponentModel.Description("Pasarela en uso: mock (simulada, con simulador) o stripe.")]
    string Provider = "mock");

public sealed record LicensesSummaryDto(
    int Total,
    int Trial,
    int Paying,
    int Grace,
    int Unpaid,
    int Inactive,
    int OwedCents,
    int CollectedCents,
    int MonthlyRecurringCents,
    int PendingCheckouts);

public sealed record AdminLicenseDto(
    Guid Id,
    Guid ClubId,
    string ClubName,
    [property: System.ComponentModel.Description("Equipos que cubre la licencia: el primer equipo y sus filiales.")]
    IReadOnlyList<string> TeamNames,
    string State,
    bool HasAccess,
    LicensePlan EffectivePlan,
    LicensePlan? Plan,
    BillingInterval? Interval,
    bool AutoRenew,
    LicensePlan? NextPlan,
    BillingInterval? NextInterval,
    DateOnly TrialEndsOn,
    DateOnly? PaidUntil,
    DateOnly? RenewsOn,
    int? NextChargeCents,
    int OwedCents,
    DateOnly? OwedSince,
    int PaidCents,
    DateTimeOffset? LastPaymentAt,
    string? Provider);

public sealed record AdminLicensePaymentDto(
    Guid Id,
    string ClubName,
    LicensePaymentKind Kind,
    LicensePlan Plan,
    BillingInterval Interval,
    int AmountCents,
    int PeriodPriceCents,
    PaymentStatus Status,
    string Provider,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? FailureReason = null);

public sealed record GetLicensesOverviewQuery(int Payments = 100) : IQuery<LicensesOverviewDto>;

internal sealed class GetLicensesOverviewHandler(
    ILicenseStore licenses,
    IClubStore clubs,
    ITeamStore teams,
    ILicensingCatalog catalog,
    ILicensePaymentGateway gateway,
    TimeProvider clock) : IQueryHandler<GetLicensesOverviewQuery, LicensesOverviewDto>
{
    public async Task<LicensesOverviewDto> Handle(GetLicensesOverviewQuery query, CancellationToken cancellationToken)
    {
        var settings = await catalog.GetAsync(cancellationToken);
        var today = SquadAccess.Today(clock);
        var all = await licenses.GetAllAsync(cancellationToken);
        var payments = await licenses.GetAllPaymentsAsync(cancellationToken);

        var clubNames = new Dictionary<Guid, string>();
        var clubTeams = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var clubId in all.Select(l => l.ClubId).Concat(payments.Select(p => p.ClubId)).Distinct())
        {
            var club = await clubs.GetByIdAsync(clubId, cancellationToken);
            clubNames[clubId] = club?.Name ?? "Club eliminado";
            clubTeams[clubId] = club?.Teams.OrderBy(t => t.AddedAt).Select(t => t.TeamId).ToList() ?? [];
        }
        var teamNameById = (await teams.GetByIdsAsync(clubTeams.Values.SelectMany(ids => ids).Distinct(), cancellationToken))
            .ToDictionary(t => t.Id, t => t.Name);
        var teamNames = clubTeams.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Select(id => teamNameById.GetValueOrDefault(id)).OfType<string>().ToList());

        var paidByLicense = payments.Where(p => p.IsPaid).GroupBy(p => p.LicenseId)
            .ToDictionary(g => g.Key, g => (Total: g.Sum(p => p.AmountCents), Last: g.Max(p => p.PaidAt)));

        var rows = all
            .Select(license => ToDto(license, settings, today, clubNames, teamNames, paidByLicense.GetValueOrDefault(license.Id)))
            .OrderBy(r => StateOrder(r.State))
            .ThenBy(r => r.ClubName)
            .ToList();

        var monthly = all
            .Where(l => l.IsRenewing && l.StatusOn(today) == LicenseStatus.Active && l.PaidUntil >= today)
            .Sum(l => l.Interval == BillingInterval.Yearly
                ? settings.PriceCents(l.EffectivePlan, BillingInterval.Yearly) / 12
                : settings.PriceCents(l.EffectivePlan, BillingInterval.Monthly));

        var summary = new LicensesSummaryDto(
            rows.Count,
            rows.Count(r => r.State is LicenseBillingState.Trial or LicenseBillingState.TrialWithPlan),
            rows.Count(r => r.State == LicenseBillingState.Active),
            rows.Count(r => r.State == LicenseBillingState.Grace),
            rows.Count(r => r.State == LicenseBillingState.Unpaid),
            rows.Count(r => r.State is LicenseBillingState.Cancelled or LicenseBillingState.TrialEnded),
            rows.Sum(r => r.OwedCents),
            payments.Where(p => p.IsPaid).Sum(p => p.AmountCents),
            monthly,
            payments.Count(p => p.Status == PaymentStatus.Pending && p.Kind == LicensePaymentKind.Subscription));

        var recent = payments
            .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
            .Take(Math.Clamp(query.Payments, 1, 500))
            .Select(p => new AdminLicensePaymentDto(
                p.Id,
                clubNames.GetValueOrDefault(p.ClubId, "Club eliminado"),
                p.Kind, p.Plan, p.Interval, p.AmountCents, p.PeriodPriceCents, p.Status, p.Provider, p.CreatedAt, p.PaidAt, p.FailureReason))
            .ToList();

        return new LicensesOverviewDto(summary, rows, recent, settings.Currency, today, gateway.Provider);
    }

    internal static AdminLicenseDto ToDto(
        ClubLicense license,
        LicensingSettings settings,
        DateOnly today,
        IReadOnlyDictionary<Guid, string> clubNames,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> teamNames,
        (int Total, DateTimeOffset? Last) paid)
    {
        var status = license.StatusOn(today);
        var state = StateOf(license, status, today);

        // Lo que se cobra en la próxima renovación (con el cambio de plan pedido, si lo hay).
        int? nextCharge = license.Plan is not null && license.Interval is { } interval
            ? settings.PriceCents(license.NextPlan ?? license.EffectivePlan, license.NextInterval ?? interval)
            : null;
        var owes = state is LicenseBillingState.Grace or LicenseBillingState.Unpaid;

        return new AdminLicenseDto(
            license.Id,
            license.ClubId,
            clubNames.GetValueOrDefault(license.ClubId, "Club eliminado"),
            teamNames.GetValueOrDefault(license.ClubId, []),
            state,
            status != LicenseStatus.Expired,
            license.EffectivePlan,
            license.Plan,
            license.Interval,
            license.AutoRenew,
            license.NextPlan,
            license.NextInterval,
            license.TrialEndsOn,
            license.PaidUntil,
            license.RenewsOn,
            nextCharge,
            owes ? nextCharge ?? 0 : 0,
            owes ? license.ValidUntil.AddDays(1) : null,
            paid.Total,
            paid.Last,
            license.Provider);
    }

    private static string StateOf(ClubLicense license, LicenseStatus status, DateOnly today) => status switch
    {
        LicenseStatus.Trial => license.Plan is null ? LicenseBillingState.Trial : LicenseBillingState.TrialWithPlan,
        LicenseStatus.Active when license.RenewsOn is { } renews && renews <= today => LicenseBillingState.Grace,
        LicenseStatus.Active => LicenseBillingState.Active,
        _ when license.Plan is null => LicenseBillingState.TrialEnded,
        _ when license.AutoRenew => LicenseBillingState.Unpaid,
        _ => LicenseBillingState.Cancelled,
    };

    private static int StateOrder(string state) => state switch
    {
        LicenseBillingState.Unpaid => 0,
        LicenseBillingState.Grace => 1,
        LicenseBillingState.TrialWithPlan => 2,
        LicenseBillingState.Trial => 3,
        LicenseBillingState.Active => 4,
        LicenseBillingState.TrialEnded => 5,
        _ => 6,
    };
}
