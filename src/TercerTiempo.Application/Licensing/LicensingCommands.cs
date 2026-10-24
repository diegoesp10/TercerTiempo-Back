using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Licensing;

public sealed record TeamLicenseDto(
    Guid TeamId,
    string TeamName,
    Guid? SquadId,
    LicenseStatus Status,
    DateOnly TrialEndsOn,
    DateOnly? PaidUntil,
    [property: Description("Último día con acceso (fin de la prueba o de lo pagado).")]
    DateOnly ValidUntil,
    [property: Description("Días que quedan de acceso; 0 si ha caducado.")]
    int DaysLeft,
    [property: Description("Plan contratado; null mientras no se ha elegido ninguno (la prueba).")]
    LicensePlan? Plan,
    [property: Description("Plan con el que funciona el equipo ahora: el contratado o Premium durante la prueba.")]
    LicensePlan EffectivePlan,
    BillingInterval? Interval,
    [property: Description("true si la suscripción se renueva sola al acabar el periodo.")]
    bool AutoRenew,
    [property: Description("Cambio de plan pedido para la próxima renovación.")]
    LicensePlan? NextPlan,
    BillingInterval? NextInterval,
    [property: Description("Día del próximo cobro, si se renueva.")]
    DateOnly? RenewsOn,
    [property: Description("Importe del próximo cobro, si se renueva.")]
    int? RenewalPriceCents,
    [property: Description("Técnicos de la temporada (nombramientos vigentes o pendientes de canjear).")]
    int StaffCount,
    [property: Description("Técnicos que admite el plan.")]
    int StaffLimit);

public sealed record LicensePlanDto(
    LicensePlan Plan,
    int MonthlyPriceCents,
    int YearlyPriceCents,
    [property: Description("Técnicos que admite el plan en cada equipo.")]
    int MaxStaff);

public sealed record LicensingCatalogDto(
    IReadOnlyList<LicensePlanDto> Plans,
    string Currency,
    int TrialDays,
    [property: Description("Días que se mantiene el acceso tras el fin de lo pagado mientras se cobra la renovación.")]
    int RenewalGraceDays,
    [property: Description("Pasarela de pago en uso; «mock» = pago simulado, sin cobro real.")]
    string Provider);

public sealed record LicensingStatusDto(
    [property: Description("true si la cuenta pertenece a algún equipo con licencia vigente (prueba o pagada).")]
    bool Licensed,
    IReadOnlyList<TeamLicenseDto> Teams,
    LicensingCatalogDto Catalog);

public sealed record LicenseCheckoutDto(
    string SessionId,
    [property: Description("Página a la que llevar al club para pagar. Con la pasarela simulada es una ruta del propio frontal.")]
    string Url,
    Guid TeamId,
    string TeamName,
    LicensePlan Plan,
    BillingInterval Interval,
    [property: Description("Lo que se cobra hoy (0 si aún queda prueba o periodo pagado).")]
    int AmountCents,
    [property: Description("Precio de cada periodo (mes o año).")]
    int PeriodPriceCents,
    [property: Description("Día del primer cobro.")]
    DateOnly FirstChargeOn,
    string Currency,
    string Provider,
    PaymentStatus Status);

public sealed record GetLicensingCatalogQuery : IQuery<LicensingCatalogDto>;

public sealed record GetLicensingStatusQuery : IQuery<LicensingStatusDto>;

public sealed record ListClubLicensesQuery : IQuery<IReadOnlyList<TeamLicenseDto>>;

public sealed record BeginLicenseCheckoutCommand(
    Guid TeamId,
    [property: Description("Basic o Premium.")]
    LicensePlan Plan,
    [property: Description("Monthly (cada mes) o Yearly (un pago al año).")]
    BillingInterval Interval) : ICommand<LicenseCheckoutDto>;

public sealed record GetLicenseCheckoutQuery(string SessionId) : IQuery<LicenseCheckoutDto>;

public sealed record ConfirmLicensePaymentCommand(string SessionId) : ICommand<TeamLicenseDto>;

public sealed record ChangeLicenseRenewalCommand(Guid TeamId, LicensePlan Plan, BillingInterval Interval) : ICommand<TeamLicenseDto>;

public sealed record CancelLicenseRenewalCommand(Guid TeamId) : ICommand<TeamLicenseDto>;

public sealed record ResumeLicenseRenewalCommand(Guid TeamId) : ICommand<TeamLicenseDto>;

public sealed record HandleLicenseWebhookCommand(string Payload, string? Signature) : ICommand<bool>;

public sealed record RenewDueLicensesCommand : ICommand<int>;

internal sealed class BeginLicenseCheckoutValidator : AbstractValidator<BeginLicenseCheckoutCommand>
{
    public BeginLicenseCheckoutValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Plan).IsInEnum().WithCatalogMessage(ErrorCode.LicensePlanInvalid);
        RuleFor(x => x.Interval).IsInEnum().WithCatalogMessage(ErrorCode.LicensePlanInvalid);
    }
}

internal sealed class ChangeLicenseRenewalValidator : AbstractValidator<ChangeLicenseRenewalCommand>
{
    public ChangeLicenseRenewalValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Plan).IsInEnum().WithCatalogMessage(ErrorCode.LicensePlanInvalid);
        RuleFor(x => x.Interval).IsInEnum().WithCatalogMessage(ErrorCode.LicensePlanInvalid);
    }
}

internal static class LicenseTexts
{
    public static string Plan(LicensePlan plan) => plan == LicensePlan.Premium ? "Premium" : "Básica";

    public static string Staff(int count) => count == 1 ? "1 técnico" : $"{count} técnicos";
}

// Cuenta el cuerpo técnico de un equipo y monta la vista de su licencia (plan, renovación y límites).
internal sealed class LicenseDescriber(
    ITeamStore teams,
    ISquadStore squads,
    ICompetitionStore competitions,
    ICoachAppointmentStore appointments,
    IOptions<LicensingOptions> options,
    TimeProvider clock)
{
    public LicensingCatalogDto Catalog()
    {
        var o = options.Value;
        LicensePlanDto Plan(LicensePlan plan) => new(plan, o.For(plan).MonthlyPriceCents, o.For(plan).YearlyPriceCents, o.For(plan).MaxStaff);
        return new([Plan(LicensePlan.Basic), Plan(LicensePlan.Premium)], o.Currency, o.TrialDays, TeamLicense.RenewalGraceDays, o.Provider);
    }

    // Nombramientos de la temporada en curso que ocupan plaza: vigentes o pendientes de canjear.
    public async Task<int> StaffCountAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var season = (await competitions.GetByTeamAsync(teamId, cancellationToken))?.CurrentSeason;
        if (season is null) return 0;
        var today = SquadAccess.Today(clock);
        return (await appointments.GetByTeamAndSeasonAsync(teamId, season.Id, cancellationToken))
            .Count(a => a.StatusOn(today) is AppointmentStatus.Pending or AppointmentStatus.Active);
    }

    // El plan elegido debe admitir el cuerpo técnico que ya tiene el equipo.
    public async Task EnsureStaffFitsAsync(Guid teamId, LicensePlan plan, CancellationToken cancellationToken)
    {
        var count = await StaffCountAsync(teamId, cancellationToken);
        var limit = options.Value.For(plan).MaxStaff;
        if (count > limit)
            throw new ConflictException(ErrorCode.LicensePlanTooSmall, LicenseTexts.Staff(count), LicenseTexts.Plan(plan), LicenseTexts.Staff(limit));
    }

    public async Task<TeamLicenseDto> DescribeAsync(TeamLicense license, CancellationToken cancellationToken)
    {
        var today = SquadAccess.Today(clock);
        var o = options.Value;
        var team = await teams.GetByIdAsync(license.TeamId, cancellationToken);
        var season = (await competitions.GetByTeamAsync(license.TeamId, cancellationToken))?.CurrentSeason;
        var squad = season is null ? null : await squads.GetByTeamAndSeasonAsync(license.TeamId, season.Id, cancellationToken);

        int? renewalPrice = license.IsRenewing
            ? o.PriceCents(license.NextPlan ?? license.EffectivePlan, license.NextInterval ?? license.Interval ?? BillingInterval.Monthly)
            : null;

        return new TeamLicenseDto(
            license.TeamId, team?.Name ?? "Equipo", squad?.Id, license.StatusOn(today),
            license.TrialEndsOn, license.PaidUntil, license.ValidUntil,
            Math.Max(0, license.ValidUntil.DayNumber - today.DayNumber + 1),
            license.Plan, license.EffectivePlan, license.Interval, license.AutoRenew, license.NextPlan, license.NextInterval,
            license.RenewsOn, renewalPrice,
            await StaffCountAsync(license.TeamId, cancellationToken), o.For(license.StaffPlan).MaxStaff);
    }

    public async Task<LicenseCheckoutDto> DescribeAsync(LicensePayment payment, TeamLicense? license, string url, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(payment.TeamId, cancellationToken);
        // Sin cobro hoy, el primero llega al acabar lo que ya cubría la licencia (una vez dada de alta, su renovación).
        var firstCharge = DateOnly.FromDateTime(payment.CreatedAt.UtcDateTime);
        if (payment.AmountCents == 0 && license is not null)
            firstCharge = license.RenewsOn ?? license.ValidUntil.AddDays(1);
        return new LicenseCheckoutDto(
            payment.SessionId, url, payment.TeamId, team?.Name ?? "Equipo", payment.Plan, payment.Interval,
            payment.AmountCents, payment.PeriodPriceCents, firstCharge, payment.Currency, payment.Provider, payment.Status);
    }
}

// Alta de una suscripción cuando la pasarela confirma el pago: desde la vuelta del club a la página de pago o desde
// el webhook, lo que llegue antes (la otra vía ya no cambia nada).
internal sealed class LicenseCheckoutCompletion(
    ILicenseStore licenses,
    ILicensePaymentGateway gateway,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<TeamLicense> CompleteAsync(LicensePayment payment, CancellationToken cancellationToken)
    {
        var license = await licenses.GetByIdAsync(payment.LicenseId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.LicenseNotFound, payment.LicenseId);
        if (payment.IsPaid) return license;

        var result = await gateway.GetCheckoutAsync(payment.SessionId, cancellationToken);
        if (!result.Completed)
            throw new ConflictException(ErrorCode.LicensePaymentNotConfirmed);

        var now = clock.GetUtcNow();
        var today = SquadAccess.Today(clock);
        license.Subscribe(payment.Plan, payment.Interval, gateway.Provider, result.SubscriptionRef, result.CustomerRef, now);
        if (payment.AmountCents > 0)
            license.RecordPaidPeriod(result.PeriodEnd ?? license.NextPeriodEnd(payment.Interval, today), now);
        payment.MarkPaid(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return license;
    }
}

internal sealed class GetLicensingCatalogHandler(LicenseDescriber describer) : IQueryHandler<GetLicensingCatalogQuery, LicensingCatalogDto>
{
    public Task<LicensingCatalogDto> Handle(GetLicensingCatalogQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(describer.Catalog());
}

internal sealed class GetLicensingStatusHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    ILicenseStore licenses,
    IClubStore clubs,
    ISquadStore squads,
    IPlayerStore players,
    LicenseDescriber describer,
    TimeProvider clock) : IQueryHandler<GetLicensingStatusQuery, LicensingStatusDto>
{
    public async Task<LicensingStatusDto> Handle(GetLicensingStatusQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId) throw new UnauthenticatedException();
        var today = SquadAccess.Today(clock);
        var list = new List<TeamLicenseDto>();

        if (currentUser.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
        {
            foreach (var team in club.Teams.OrderBy(t => t.AddedAt))
                list.Add(await describer.DescribeAsync(await gate.EnsureForTeamAsync(club, team.TeamId, cancellationToken), cancellationToken));
        }
        else if (currentUser.CoachId is { } coachId)
        {
            foreach (var squad in await squads.GetByCoachAsync(coachId, today, cancellationToken))
                if (await LicenseOfAsync(squad.TeamId, cancellationToken) is { } license)
                    list.Add(await describer.DescribeAsync(license, cancellationToken));
        }
        else if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player
            && await squads.GetByPlayerAsync(player.Id, cancellationToken) is { } own
            && await LicenseOfAsync(own.TeamId, cancellationToken) is { } license)
        {
            list.Add(await describer.DescribeAsync(license, cancellationToken));
        }

        return new LicensingStatusDto(await gate.IsViewerLicensedAsync(currentUser, cancellationToken), list, describer.Catalog());
    }

    private async Task<TeamLicense?> LicenseOfAsync(Guid teamId, CancellationToken cancellationToken)
    {
        await gate.StatusForTeamAsync(teamId, cancellationToken);
        return await licenses.GetByTeamAsync(teamId, cancellationToken);
    }
}

internal sealed class ListClubLicensesHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    LicenseDescriber describer) : IQueryHandler<ListClubLicensesQuery, IReadOnlyList<TeamLicenseDto>>
{
    public async Task<IReadOnlyList<TeamLicenseDto>> Handle(ListClubLicensesQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var result = new List<TeamLicenseDto>();
        foreach (var clubTeam in club.Teams.OrderBy(t => t.AddedAt))
            result.Add(await describer.DescribeAsync(await gate.EnsureForTeamAsync(club, clubTeam.TeamId, cancellationToken), cancellationToken));
        return result;
    }
}

internal static class ClubLicense
{
    public static async Task<(Club Club, TeamLicense License)> OfTeamAsync(
        ICurrentUser currentUser, IClubStore clubs, ITeamStore teams, ILicenseGate gate, Guid teamId, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        if (!club.Manages(teamId))
            throw new ForbiddenException(ErrorCode.ClubTeamNotInClub);

        var team = await teams.GetByIdAsync(teamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, teamId);
        return (club, await gate.EnsureForTeamAsync(club, team.Id, cancellationToken));
    }
}

internal sealed class BeginLicenseCheckoutHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    ILicenseStore licenses,
    IClubStore clubs,
    ITeamStore teams,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IOptions<LicensingOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<BeginLicenseCheckoutCommand, LicenseCheckoutDto>
{
    public async Task<LicenseCheckoutDto> Handle(BeginLicenseCheckoutCommand command, CancellationToken cancellationToken)
    {
        var (club, license) = await ClubLicense.OfTeamAsync(currentUser, clubs, teams, gate, command.TeamId, cancellationToken);
        var today = SquadAccess.Today(clock);

        if (license.IsRenewing && license.IsActiveOn(today))
            throw new ConflictException(ErrorCode.LicenseSubscriptionActive);
        await describer.EnsureStaffFitsAsync(license.TeamId, command.Plan, cancellationToken);

        // Lo que queda de prueba o de un periodo ya pagado no se pierde: el primer cobro llega al acabarse.
        var deferUntil = license.IsActiveOn(today) ? license.ValidUntil : (DateOnly?)null;
        var o = options.Value;
        var price = o.PriceCents(command.Plan, command.Interval);
        var team = await teams.GetByIdAsync(license.TeamId, cancellationToken);

        var session = await gateway.BeginCheckoutAsync(
            new CheckoutRequest(club.Id, license.TeamId, license.Id, team?.Name ?? "Equipo", command.Plan, command.Interval, price, o.Currency, deferUntil, license.CustomerRef),
            cancellationToken);
        var payment = LicensePayment.Begin(
            license, LicensePaymentKind.Subscription, command.Plan, command.Interval, deferUntil is null ? price : 0, price,
            o.Currency, gateway.Provider, session.SessionId, clock.GetUtcNow());
        licenses.AddPayment(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await describer.DescribeAsync(payment, license, session.Url, cancellationToken);
    }
}

internal sealed class GetLicenseCheckoutHandler(
    ICurrentUser currentUser,
    ILicenseStore licenses,
    IClubStore clubs,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer) : IQueryHandler<GetLicenseCheckoutQuery, LicenseCheckoutDto>
{
    public async Task<LicenseCheckoutDto> Handle(GetLicenseCheckoutQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var payment = await licenses.GetPaymentBySessionAsync(query.SessionId, cancellationToken);
        if (payment is null || payment.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.LicensePaymentNotFound, query.SessionId);
        var license = await licenses.GetByIdAsync(payment.LicenseId, cancellationToken);
        return await describer.DescribeAsync(payment, license, gateway.Provider == "mock" ? $"/pago/{payment.SessionId}" : string.Empty, cancellationToken);
    }
}

internal sealed class ConfirmLicensePaymentHandler(
    ICurrentUser currentUser,
    ILicenseStore licenses,
    IClubStore clubs,
    LicenseCheckoutCompletion completion,
    LicenseDescriber describer) : ICommandHandler<ConfirmLicensePaymentCommand, TeamLicenseDto>
{
    public async Task<TeamLicenseDto> Handle(ConfirmLicensePaymentCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var payment = await licenses.GetPaymentBySessionAsync(command.SessionId, cancellationToken);
        if (payment is null || payment.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.LicensePaymentNotFound, command.SessionId);

        var license = await completion.CompleteAsync(payment, cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

internal sealed class ChangeLicenseRenewalHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    ITeamStore teams,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ChangeLicenseRenewalCommand, TeamLicenseDto>
{
    public async Task<TeamLicenseDto> Handle(ChangeLicenseRenewalCommand command, CancellationToken cancellationToken)
    {
        var (_, license) = await ClubLicense.OfTeamAsync(currentUser, clubs, teams, gate, command.TeamId, cancellationToken);
        await describer.EnsureStaffFitsAsync(license.TeamId, command.Plan, cancellationToken);

        license.ChangeRenewal(command.Plan, command.Interval, SquadAccess.Today(clock), clock.GetUtcNow());
        if (license.SubscriptionRef is { } subscription)
            await gateway.ChangeRenewalAsync(subscription, command.Plan, command.Interval, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

internal sealed class CancelLicenseRenewalHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    ITeamStore teams,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CancelLicenseRenewalCommand, TeamLicenseDto>
{
    public async Task<TeamLicenseDto> Handle(CancelLicenseRenewalCommand command, CancellationToken cancellationToken)
    {
        var (_, license) = await ClubLicense.OfTeamAsync(currentUser, clubs, teams, gate, command.TeamId, cancellationToken);

        license.CancelRenewal(clock.GetUtcNow());
        if (license.SubscriptionRef is { } subscription)
            await gateway.CancelRenewalAsync(subscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

internal sealed class ResumeLicenseRenewalHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    ITeamStore teams,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ResumeLicenseRenewalCommand, TeamLicenseDto>
{
    public async Task<TeamLicenseDto> Handle(ResumeLicenseRenewalCommand command, CancellationToken cancellationToken)
    {
        var (_, license) = await ClubLicense.OfTeamAsync(currentUser, clubs, teams, gate, command.TeamId, cancellationToken);
        await describer.EnsureStaffFitsAsync(license.TeamId, license.EffectivePlan, cancellationToken);

        license.ResumeRenewal(SquadAccess.Today(clock), clock.GetUtcNow());
        if (license.SubscriptionRef is { } subscription)
            await gateway.ResumeRenewalAsync(subscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

// Avisos de la pasarela: alta completada, factura cobrada (renovaciones) y suscripción terminada.
internal sealed class HandleLicenseWebhookHandler(
    ILicenseStore licenses,
    ILicensePaymentGateway gateway,
    LicenseCheckoutCompletion completion,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<HandleLicenseWebhookCommand, bool>
{
    public async Task<bool> Handle(HandleLicenseWebhookCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        switch (gateway.ParseWebhook(command.Payload, command.Signature))
        {
            case CheckoutCompletedEvent completed:
                if (await licenses.GetPaymentBySessionAsync(completed.SessionId, cancellationToken) is not { } payment) return false;
                await completion.CompleteAsync(payment, cancellationToken);
                return true;

            case InvoicePaidEvent invoice:
                var license = invoice.LicenseId is { } licenseId ? await licenses.GetByIdAsync(licenseId, cancellationToken) : null;
                license ??= await licenses.GetBySubscriptionAsync(invoice.SubscriptionRef, cancellationToken);
                if (license is null || invoice.AmountCents == 0) return false;
                if (await licenses.GetPaymentBySessionAsync(invoice.InvoiceId, cancellationToken) is not null) return false;

                // La primera factura de una suscripción sin prueba ya está apuntada como el pago del alta.
                if (invoice.BillingReason == "subscription_create")
                {
                    license.RecordPaidPeriod(invoice.PeriodEnd, now);
                }
                else
                {
                    license.RecordRenewal(invoice.PeriodEnd, now);
                    var renewal = LicensePayment.Begin(
                        license, LicensePaymentKind.Renewal, license.EffectivePlan, license.Interval ?? BillingInterval.Monthly,
                        invoice.AmountCents, invoice.AmountCents, invoice.Currency, gateway.Provider, invoice.InvoiceId, now);
                    renewal.MarkPaid(now);
                    licenses.AddPayment(renewal);
                }
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return true;

            case SubscriptionEndedEvent ended:
                if (await licenses.GetBySubscriptionAsync(ended.SubscriptionRef, cancellationToken) is not { } endedLicense) return false;
                endedLicense.EndSubscription(now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return true;

            default:
                return false;
        }
    }
}

// Renovaciones con la pasarela simulada: cobra (sin cobro real) el periodo siguiente de las suscripciones que han
// llegado a su día de renovación, como haría Stripe. Con Stripe no hace nada: avisa su webhook.
internal sealed class RenewDueLicensesHandler(
    ILicenseStore licenses,
    ILicensePaymentGateway gateway,
    IOptions<LicensingOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RenewDueLicensesCommand, int>
{
    public async Task<int> Handle(RenewDueLicensesCommand command, CancellationToken cancellationToken)
    {
        if (gateway.RenewsByWebhook) return 0;

        var today = SquadAccess.Today(clock);
        var now = clock.GetUtcNow();
        var o = options.Value;
        var renewed = 0;

        foreach (var license in await licenses.GetRenewingAsync(gateway.Provider, cancellationToken))
        {
            if (license.RenewsOn is not { } due || due > today) continue;

            var plan = license.NextPlan ?? license.EffectivePlan;
            var interval = license.NextInterval ?? license.Interval ?? BillingInterval.Monthly;
            var price = o.PriceCents(plan, interval);

            var payment = LicensePayment.Begin(
                license, LicensePaymentKind.Renewal, plan, interval, price, price, o.Currency, gateway.Provider, $"mockinv_{Guid.NewGuid():N}", now);
            payment.MarkPaid(now);
            licenses.AddPayment(payment);
            license.RecordRenewal(license.RenewalPeriodEnd(interval, today), now);
            renewed++;
        }

        if (renewed > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return renewed;
    }
}
