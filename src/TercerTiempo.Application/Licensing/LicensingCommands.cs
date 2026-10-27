using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Licensing;

public sealed record LicensedTeamDto(
    Guid TeamId,
    string TeamName,
    string? TeamCrestUrl,
    Guid? SquadId,
    [property: Description("Técnicos de la temporada en este equipo (nombramientos vigentes o pendientes de canjear).")]
    int StaffCount);

public sealed record ClubLicenseDto(
    Guid ClubId,
    string ClubName,
    LicenseStatus Status,
    DateOnly TrialEndsOn,
    DateOnly? PaidUntil,
    [property: Description("Último día con acceso (fin de la prueba o de lo pagado).")]
    DateOnly ValidUntil,
    [property: Description("Días que quedan de acceso; 0 si ha caducado.")]
    int DaysLeft,
    [property: Description("true una vez contratada la licencia de club; antes, la prueba.")]
    bool Contracted,
    BillingInterval? Interval,
    [property: Description("true si la suscripción se renueva sola al acabar el periodo.")]
    bool AutoRenew,
    [property: Description("Cambio de facturación pedido para la próxima renovación.")]
    BillingInterval? NextInterval,
    [property: Description("Licencias de técnico contratadas: la bolsa del club para todos sus equipos.")]
    int StaffSeats,
    [property: Description("Técnicos que quedarán en la próxima renovación si se ha pedido reducirlos.")]
    int? NextStaffSeats,
    [property: Description("Técnicos que admite ahora la licencia: los de la prueba mientras no se ha contratado; después, los contratados.")]
    int StaffLimit,
    [property: Description("Técnicos con nombramiento vigente o pendiente de canjear en todos los equipos del club.")]
    int StaffInUse,
    [property: Description("Día del próximo cobro, si se renueva.")]
    DateOnly? RenewsOn,
    [property: Description("Importe del próximo cobro, si se renueva.")]
    int? RenewalPriceCents,
    [property: Description("Equipos que cubre la licencia (el primer equipo y sus filiales) con el cuerpo técnico de cada uno. Para un técnico o un jugador, solo los suyos.")]
    IReadOnlyList<LicensedTeamDto> Teams);

public sealed record LicensePriceDto(int MonthlyPriceCents, int YearlyPriceCents);

public sealed record LicensingCatalogDto(
    [property: Description("Licencia de club: el primer equipo y sus filiales, con todas las funciones para el club.")]
    LicensePriceDto Club,
    [property: Description("Cada licencia de técnico: una cuenta del cuerpo técnico en cualquiera de los equipos del club.")]
    LicensePriceDto StaffSeat,
    string Currency,
    int TrialDays,
    [property: Description("Técnicos que admite la prueba.")]
    int TrialStaffSeats,
    [property: Description("Licencias de técnico que puede tener un club como mucho.")]
    int MaxStaffSeats,
    [property: Description("Días que se mantiene el acceso tras el fin de lo pagado mientras se cobra la renovación.")]
    int RenewalGraceDays,
    [property: Description("Pasarela de pago en uso; «mock» = pago simulado, sin cobro real.")]
    string Provider);

public sealed record LicensingStatusDto(
    [property: Description("true si la cuenta pertenece a algún equipo con licencia vigente (prueba o pagada).")]
    bool Licensed,
    [property: Description("Licencias de los clubes de la cuenta (club: la suya; técnico o jugador: las de los clubes de sus equipos).")]
    IReadOnlyList<ClubLicenseDto> Licenses,
    LicensingCatalogDto Catalog);

public sealed record LicenseCheckoutDto(
    string SessionId,
    [property: Description("Página a la que llevar al club para pagar. Con la pasarela simulada es una ruta del propio frontal.")]
    string Url,
    Guid ClubId,
    string ClubName,
    [property: Description("Equipos que cubre la licencia: el primer equipo y sus filiales.")]
    IReadOnlyList<string> TeamNames,
    BillingInterval Interval,
    [property: Description("Licencias de técnico que se contratan con la de club.")]
    int StaffSeats,
    [property: Description("Lo que se cobra hoy (0 si aún queda prueba o periodo pagado).")]
    int AmountCents,
    [property: Description("Precio de cada periodo (mes o año): la licencia de club más los técnicos.")]
    int PeriodPriceCents,
    [property: Description("Día del primer cobro.")]
    DateOnly FirstChargeOn,
    string Currency,
    string Provider,
    PaymentStatus Status,
    [property: Description("Avisos de la pasarela de esta licencia desde que se abrió el pago (la cola de eventos), del más reciente al primero.")]
    IReadOnlyList<PaymentEventDto>? Events = null);

public sealed record GetLicensingCatalogQuery : IQuery<LicensingCatalogDto>;

public sealed record GetLicensingStatusQuery : IQuery<LicensingStatusDto>;

public sealed record GetClubLicenseQuery : IQuery<ClubLicenseDto>;

public sealed record BeginLicenseCheckoutCommand(
    [property: Description("Monthly (cada mes) o Yearly (un pago al año).")]
    BillingInterval Interval,
    [property: Description("Licencias de técnico que se contratan ya con la de club (0 = solo el club; se pueden añadir después).")]
    int StaffSeats) : ICommand<LicenseCheckoutDto>;

public sealed record GetLicenseCheckoutQuery(string SessionId) : IQuery<LicenseCheckoutDto>;

public sealed record ConfirmLicensePaymentCommand(string SessionId) : ICommand<ClubLicenseDto>;

public sealed record ChangeLicenseRenewalCommand(BillingInterval Interval) : ICommand<ClubLicenseDto>;

public sealed record ChangeStaffSeatsCommand(
    [property: Description("Licencias de técnico que quiere tener el club.")]
    int StaffSeats) : ICommand<ClubLicenseDto>;

public sealed record CancelLicenseRenewalCommand : ICommand<ClubLicenseDto>;

public sealed record ResumeLicenseRenewalCommand : ICommand<ClubLicenseDto>;

public sealed record HandleLicenseWebhookCommand(string Payload, string? Signature) : ICommand<bool>;

public sealed record RenewDueLicensesCommand : ICommand<int>;

internal sealed class BeginLicenseCheckoutValidator : AbstractValidator<BeginLicenseCheckoutCommand>
{
    public BeginLicenseCheckoutValidator()
    {
        RuleFor(x => x.Interval).IsInEnum().WithCatalogMessage(ErrorCode.LicensePlanInvalid);
        RuleFor(x => x.StaffSeats).GreaterThanOrEqualTo(0).WithName("Técnicos");
    }
}

internal sealed class ChangeLicenseRenewalValidator : AbstractValidator<ChangeLicenseRenewalCommand>
{
    public ChangeLicenseRenewalValidator() => RuleFor(x => x.Interval).IsInEnum().WithCatalogMessage(ErrorCode.LicensePlanInvalid);
}

internal sealed class ChangeStaffSeatsValidator : AbstractValidator<ChangeStaffSeatsCommand>
{
    public ChangeStaffSeatsValidator() => RuleFor(x => x.StaffSeats).GreaterThanOrEqualTo(0).WithName("Técnicos");
}

internal static class LicenseTexts
{
    public static string Staff(int count) => count == 1 ? "1 técnico" : $"{count} técnicos";

    public static string Subscription(int staffSeats) => staffSeats > 0 ? $"club + {Staff(staffSeats)}" : "club";
}

// Cuenta el cuerpo técnico de los equipos del club y monta la vista de la licencia (renovación, técnicos contratados y
// en uso, equipos que cubre).
internal sealed class LicenseDescriber(
    IClubStore clubs,
    ITeamStore teams,
    ISquadStore squads,
    ICompetitionStore competitions,
    ICoachAppointmentStore appointments,
    ILicensingCatalog catalog,
    IOptions<LicensingOptions> options,
    TimeProvider clock)
{
    public async Task<LicensingCatalogDto> CatalogAsync(CancellationToken cancellationToken)
    {
        var o = await catalog.GetAsync(cancellationToken);
        LicensePriceDto Price(LicenseItem item) => new(o.For(item).MonthlyPriceCents, o.For(item).YearlyPriceCents);
        return new(Price(LicenseItem.Club), Price(LicenseItem.StaffSeat), o.Currency, o.TrialDays, o.TrialStaffSeats, o.MaxStaffSeats,
            ClubLicense.RenewalGraceDays, options.Value.Provider);
    }

    // Nombramientos de la temporada en curso que ocupan plaza: vigentes o pendientes de canjear.
    public async Task<int> StaffCountAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var season = (await competitions.GetByTeamAsync(teamId, cancellationToken: cancellationToken))?.CurrentSeason;
        if (season is null) return 0;
        var today = SquadAccess.Today(clock);
        return (await appointments.GetByTeamAndSeasonAsync(teamId, season.Id, cancellationToken))
            .Count(a => a.StatusOn(today) is AppointmentStatus.Pending or AppointmentStatus.Active);
    }

    // Las licencias de técnico son una bolsa del club: cuentan los técnicos de todos sus equipos.
    public async Task<int> ClubStaffCountAsync(Club club, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var clubTeam in club.Teams)
            total += await StaffCountAsync(clubTeam.TeamId, cancellationToken);
        return total;
    }

    // Los técnicos de la licencia deben cubrir los nombramientos que ya tiene el club.
    public async Task EnsureStaffFitsAsync(Club club, int staffSeats, CancellationToken cancellationToken)
    {
        var inUse = await ClubStaffCountAsync(club, cancellationToken);
        if (inUse > staffSeats)
            throw new ConflictException(ErrorCode.LicenseStaffSeatsTooFew, LicenseTexts.Staff(inUse), LicenseTexts.Staff(staffSeats));
    }

    // onlyTeams: para un técnico o un jugador, sus equipos (no se le enseña el cuerpo técnico del resto del club).
    public async Task<ClubLicenseDto> DescribeAsync(ClubLicense license, CancellationToken cancellationToken, IReadOnlySet<Guid>? onlyTeams = null)
    {
        var today = SquadAccess.Today(clock);
        var o = await catalog.GetAsync(cancellationToken);
        var club = await clubs.GetByIdAsync(license.ClubId, cancellationToken);

        var covered = new List<LicensedTeamDto>();
        foreach (var clubTeam in club?.Teams.OrderBy(t => t.AddedAt) ?? Enumerable.Empty<ClubTeam>())
        {
            if (onlyTeams is not null && !onlyTeams.Contains(clubTeam.TeamId)) continue;
            var team = await teams.GetByIdAsync(clubTeam.TeamId, cancellationToken);
            if (team is null) continue;
            var season = (await competitions.GetByTeamAsync(team.Id, cancellationToken: cancellationToken))?.CurrentSeason;
            var squad = season is null ? null : await squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken);
            covered.Add(new LicensedTeamDto(team.Id, team.Name, team.CrestLink(), squad?.Id, await StaffCountAsync(team.Id, cancellationToken)));
        }

        int? renewalPrice = license.IsRenewing
            ? o.SubscriptionPriceCents(license.NextInterval ?? license.Interval!.Value, license.RenewalStaffSeats)
            : null;

        return new ClubLicenseDto(
            license.ClubId, club?.Name ?? "Club", license.StatusOn(today),
            license.TrialEndsOn, license.PaidUntil, license.ValidUntil,
            Math.Max(0, license.ValidUntil.DayNumber - today.DayNumber + 1),
            license.IsContracted, license.Interval, license.AutoRenew, license.NextInterval,
            license.StaffSeats, license.NextStaffSeats, license.StaffLimit(o.TrialStaffSeats), covered.Sum(t => t.StaffCount),
            license.RenewsOn, renewalPrice, covered);
    }

    public async Task<LicenseCheckoutDto> DescribeAsync(LicensePayment payment, ClubLicense? license, string url, CancellationToken cancellationToken)
    {
        var club = await clubs.GetByIdAsync(payment.ClubId, cancellationToken);
        var teamNames = new List<string>();
        foreach (var clubTeam in club?.Teams.OrderBy(t => t.AddedAt) ?? Enumerable.Empty<ClubTeam>())
            if (await teams.GetByIdAsync(clubTeam.TeamId, cancellationToken) is { } team)
                teamNames.Add(team.Name);

        // Sin cobro hoy, el primero llega al acabar lo que ya cubría la licencia (una vez dada de alta, su renovación).
        var firstCharge = DateOnly.FromDateTime(payment.CreatedAt.UtcDateTime);
        if (payment.AmountCents == 0 && license is not null)
            firstCharge = license.RenewsOn ?? license.ValidUntil.AddDays(1);
        return new LicenseCheckoutDto(
            payment.SessionId, url, payment.ClubId, club?.Name ?? "Club", teamNames, payment.Interval, payment.StaffSeats,
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
    public async Task<ClubLicense> CompleteAsync(LicensePayment payment, CancellationToken cancellationToken)
    {
        var license = await licenses.GetByIdAsync(payment.LicenseId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.LicenseNotFound, payment.LicenseId);
        if (payment.IsPaid) return license;

        var result = await gateway.GetCheckoutAsync(payment.SessionId, cancellationToken);
        if (!result.Completed)
            throw new ConflictException(ErrorCode.LicensePaymentNotConfirmed);

        var now = clock.GetUtcNow();
        var today = SquadAccess.Today(clock);
        license.Subscribe(payment.Interval, payment.StaffSeats, gateway.Provider, result.SubscriptionRef, result.CustomerRef, now);
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
        describer.CatalogAsync(cancellationToken);
}

internal sealed class GetLicensingStatusHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
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
        var list = new List<ClubLicenseDto>();

        if (currentUser.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
        {
            list.Add(await describer.DescribeAsync(await gate.EnsureForClubAsync(club, cancellationToken), cancellationToken));
        }
        else
        {
            var ownTeams = new List<Guid>();
            if (currentUser.CoachId is { } coachId)
                ownTeams.AddRange((await squads.GetByCoachAsync(coachId, today, cancellationToken)).Select(s => s.TeamId));
            else if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player
                && await squads.GetByPlayerAsync(player.Id, cancellationToken) is { } own)
                ownTeams.Add(own.TeamId);

            // Una licencia por club, con solo los equipos de la cuenta.
            var byClub = new Dictionary<Guid, (Club Club, HashSet<Guid> Teams)>();
            foreach (var teamId in ownTeams.Distinct())
            {
                if (await clubs.GetByTeamAsync(teamId, cancellationToken) is not { } owner) continue;
                if (!byClub.TryGetValue(owner.Id, out var entry)) byClub[owner.Id] = entry = (owner, []);
                entry.Teams.Add(teamId);
            }
            foreach (var (owner, teams) in byClub.Values)
                list.Add(await describer.DescribeAsync(await gate.EnsureForClubAsync(owner, cancellationToken), cancellationToken, teams));
        }

        return new LicensingStatusDto(await gate.IsViewerLicensedAsync(currentUser, cancellationToken), list, await describer.CatalogAsync(cancellationToken));
    }
}

internal sealed class GetClubLicenseHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    LicenseDescriber describer) : IQueryHandler<GetClubLicenseQuery, ClubLicenseDto>
{
    public async Task<ClubLicenseDto> Handle(GetClubLicenseQuery query, CancellationToken cancellationToken)
    {
        var (_, license) = await ClubLicenseAccess.OfCurrentClubAsync(currentUser, clubs, gate, cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

internal static class ClubLicenseAccess
{
    public static async Task<(Club Club, ClubLicense License)> OfCurrentClubAsync(
        ICurrentUser currentUser, IClubStore clubs, ILicenseGate gate, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        return (club, await gate.EnsureForClubAsync(club, cancellationToken));
    }
}

internal sealed class BeginLicenseCheckoutHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    ILicenseStore licenses,
    IClubStore clubs,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    ILicensingCatalog catalog,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<BeginLicenseCheckoutCommand, LicenseCheckoutDto>
{
    public async Task<LicenseCheckoutDto> Handle(BeginLicenseCheckoutCommand command, CancellationToken cancellationToken)
    {
        var (club, license) = await ClubLicenseAccess.OfCurrentClubAsync(currentUser, clubs, gate, cancellationToken);
        var today = SquadAccess.Today(clock);

        if (license.IsRenewing && license.IsActiveOn(today))
            throw new ConflictException(ErrorCode.LicenseSubscriptionActive);
        var o = await catalog.GetAsync(cancellationToken);
        o.EnsureStaffSeatsAllowed(command.StaffSeats);
        await describer.EnsureStaffFitsAsync(club, command.StaffSeats, cancellationToken);

        // Lo que queda de prueba o de un periodo ya pagado no se pierde: el primer cobro llega al acabarse.
        var deferUntil = license.IsActiveOn(today) ? license.ValidUntil : (DateOnly?)null;
        var request = new CheckoutRequest(
            club.Id, license.Id, club.Name, command.Interval, command.StaffSeats,
            o.PriceCents(LicenseItem.Club, command.Interval), o.PriceCents(LicenseItem.StaffSeat, command.Interval),
            o.Currency, deferUntil, license.CustomerRef);

        var session = await gateway.BeginCheckoutAsync(request, cancellationToken);
        var payment = LicensePayment.Begin(
            license, LicensePaymentKind.Subscription, command.Interval, command.StaffSeats, deferUntil is null ? request.PeriodPriceCents : 0,
            request.PeriodPriceCents, o.Currency, gateway.Provider, session.SessionId, clock.GetUtcNow());
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
    IPaymentEventInbox inbox,
    LicenseDescriber describer) : IQueryHandler<GetLicenseCheckoutQuery, LicenseCheckoutDto>
{
    public async Task<LicenseCheckoutDto> Handle(GetLicenseCheckoutQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var payment = await licenses.GetPaymentBySessionAsync(query.SessionId, cancellationToken);
        if (payment is null || payment.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.LicensePaymentNotFound, query.SessionId);
        var license = await licenses.GetByIdAsync(payment.LicenseId, cancellationToken);
        var dto = await describer.DescribeAsync(payment, license, gateway.Provider == "mock" ? $"/pago/{payment.SessionId}" : string.Empty, cancellationToken);
        // La página de pago enseña cómo avanzan los avisos de la pasarela (el alta, el primer cobro).
        var events = await inbox.ListAsync(20, payment.LicenseId, payment.CreatedAt.AddMinutes(-1), cancellationToken);
        return dto with { Events = events };
    }
}

internal sealed class ConfirmLicensePaymentHandler(
    ICurrentUser currentUser,
    ILicenseStore licenses,
    IClubStore clubs,
    LicenseCheckoutCompletion completion,
    LicenseDescriber describer) : ICommandHandler<ConfirmLicensePaymentCommand, ClubLicenseDto>
{
    public async Task<ClubLicenseDto> Handle(ConfirmLicensePaymentCommand command, CancellationToken cancellationToken)
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
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ChangeLicenseRenewalCommand, ClubLicenseDto>
{
    public async Task<ClubLicenseDto> Handle(ChangeLicenseRenewalCommand command, CancellationToken cancellationToken)
    {
        var (_, license) = await ClubLicenseAccess.OfCurrentClubAsync(currentUser, clubs, gate, cancellationToken);

        license.ChangeRenewal(command.Interval, SquadAccess.Today(clock), clock.GetUtcNow());
        if (license.SubscriptionRef is { } subscription)
            await gateway.ChangeRenewalAsync(subscription, command.Interval, license.RenewalStaffSeats, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

// Licencias de técnico del club, una vez contratada la de club. Las que se añaden valen ya y, con un periodo pagado,
// se cobra al momento su parte proporcional hasta la renovación (llega como factura de la pasarela a la cola de
// eventos); las que se quitan, desde la próxima renovación, y nunca por debajo de los nombramientos que ya hay.
internal sealed class ChangeStaffSeatsHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    ILicensingCatalog catalog,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ChangeStaffSeatsCommand, ClubLicenseDto>
{
    public async Task<ClubLicenseDto> Handle(ChangeStaffSeatsCommand command, CancellationToken cancellationToken)
    {
        var (club, license) = await ClubLicenseAccess.OfCurrentClubAsync(currentUser, clubs, gate, cancellationToken);
        var o = await catalog.GetAsync(cancellationToken);
        o.EnsureStaffSeatsAllowed(command.StaffSeats);
        if (command.StaffSeats < license.StaffSeats)
            await describer.EnsureStaffFitsAsync(club, command.StaffSeats, cancellationToken);

        var today = SquadAccess.Today(clock);
        var added = license.ChangeStaffSeats(command.StaffSeats, today, clock.GetUtcNow());
        var interval = license.Interval!.Value;
        var chargeNow = license.StaffSeatsProrationCents(added, o.PriceCents(LicenseItem.StaffSeat, interval), today);
        if (license.SubscriptionRef is { } subscription)
            await gateway.ChangeStaffSeatsAsync(
                new StaffSeatsChange(license.Id, subscription, license.RenewalStaffSeats, chargeNow, license.ValidUntil), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

internal sealed class CancelLicenseRenewalHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CancelLicenseRenewalCommand, ClubLicenseDto>
{
    public async Task<ClubLicenseDto> Handle(CancelLicenseRenewalCommand command, CancellationToken cancellationToken)
    {
        var (_, license) = await ClubLicenseAccess.OfCurrentClubAsync(currentUser, clubs, gate, cancellationToken);

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
    ILicensePaymentGateway gateway,
    LicenseDescriber describer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ResumeLicenseRenewalCommand, ClubLicenseDto>
{
    public async Task<ClubLicenseDto> Handle(ResumeLicenseRenewalCommand command, CancellationToken cancellationToken)
    {
        var (club, license) = await ClubLicenseAccess.OfCurrentClubAsync(currentUser, clubs, gate, cancellationToken);
        await describer.EnsureStaffFitsAsync(club, license.StaffSeats, cancellationToken);

        license.ResumeRenewal(SquadAccess.Today(clock), clock.GetUtcNow());
        if (license.SubscriptionRef is { } subscription)
            await gateway.ResumeRenewalAsync(subscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await describer.DescribeAsync(license, cancellationToken);
    }
}

// Aviso de la pasarela (webhook de Stripe): se comprueba la firma y se guarda en la cola de eventos; lo aplica después
// el proceso de la cola, con reintentos. Así Stripe recibe su respuesta enseguida y un fallo al aplicarlo no lo pierde.
internal sealed class HandleLicenseWebhookHandler(
    ILicensePaymentGateway gateway,
    IPaymentEventInbox inbox,
    IPaymentEventSignal signal) : ICommandHandler<HandleLicenseWebhookCommand, bool>
{
    public async Task<bool> Handle(HandleLicenseWebhookCommand command, CancellationToken cancellationToken)
    {
        var received = gateway.ReadWebhook(command.Payload, command.Signature);
        var added = await inbox.AddAsync(new PaymentEventInput(gateway.Provider, received.ExternalId, received.Type, command.Payload, null), cancellationToken);
        if (added) signal.Signal();
        return added;
    }
}

// Renovaciones con la pasarela simulada: al llegar el día de renovación de una suscripción, la pasarela simulada genera
// en la cola el mismo aviso que mandaría Stripe (factura cobrada) y la cola lo aplica. Con Stripe no hace nada: cobra
// Stripe y avisa su webhook.
internal sealed class RenewDueLicensesHandler(
    ILicenseStore licenses,
    ILicensePaymentGateway gateway,
    IPaymentSimulator simulator,
    TimeProvider clock) : ICommandHandler<RenewDueLicensesCommand, int>
{
    public async Task<int> Handle(RenewDueLicensesCommand command, CancellationToken cancellationToken)
    {
        if (gateway.RenewsByWebhook || !simulator.Enabled) return 0;

        var today = SquadAccess.Today(clock);
        var renewed = 0;
        foreach (var license in await licenses.GetRenewingAsync(gateway.Provider, cancellationToken))
        {
            if (license.RenewsOn is not { } due || due > today) continue;
            await simulator.EmitAsync(license, SimulatedLicenseEvent.RenewalPaid, cancellationToken);
            renewed++;
        }
        return renewed;
    }
}
