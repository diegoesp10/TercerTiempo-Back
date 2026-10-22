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
    int DaysLeft);

public sealed record LicensingStatusDto(
    [property: Description("true si la cuenta pertenece a algún equipo con licencia vigente (prueba o pagada).")]
    bool Licensed,
    IReadOnlyList<TeamLicenseDto> Teams,
    int MonthlyPriceCents,
    string Currency,
    int TrialDays,
    [property: Description("Pasarela de pago en uso; «mock» = pago simulado, sin cobro real.")]
    string Provider);

public sealed record LicenseCheckoutDto(
    string SessionId,
    [property: Description("Página a la que llevar al club para pagar. Con la pasarela simulada es una ruta del propio frontal.")]
    string Url,
    Guid TeamId,
    string TeamName,
    int Months,
    int AmountCents,
    string Currency,
    string Provider,
    PaymentStatus Status);

public sealed record GetLicensingStatusQuery : IQuery<LicensingStatusDto>;

public sealed record ListClubLicensesQuery : IQuery<IReadOnlyList<TeamLicenseDto>>;

public sealed record BeginLicenseCheckoutCommand(
    Guid TeamId,
    [property: Description("Meses que se pagan (1-12).")]
    int Months) : ICommand<LicenseCheckoutDto>;

public sealed record GetLicenseCheckoutQuery(string SessionId) : IQuery<LicenseCheckoutDto>;

public sealed record ConfirmLicensePaymentCommand(string SessionId) : ICommand<TeamLicenseDto>;

internal sealed class BeginLicenseCheckoutValidator : AbstractValidator<BeginLicenseCheckoutCommand>
{
    public BeginLicenseCheckoutValidator(IOptions<LicensingOptions> options)
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Months).InclusiveBetween(1, options.Value.MaxMonths).WithCatalogMessage(ErrorCode.LicenseMonthsInvalid, options.Value.MaxMonths);
    }
}

internal static class LicenseMapper
{
    public static TeamLicenseDto ToDto(TeamLicense license, string teamName, Guid? squadId, DateOnly today) =>
        new(license.TeamId, teamName, squadId, license.StatusOn(today), license.TrialEndsOn, license.PaidUntil, license.ValidUntil,
            Math.Max(0, license.ValidUntil.DayNumber - today.DayNumber + 1));

    public static LicenseCheckoutDto ToDto(LicensePayment payment, string teamName, string url) =>
        new(payment.SessionId, url, payment.TeamId, teamName, payment.Months, payment.AmountCents, payment.Currency, payment.Provider, payment.Status);
}

internal sealed class GetLicensingStatusHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    ILicenseStore licenses,
    IClubStore clubs,
    ISquadStore squads,
    IPlayerStore players,
    ITeamStore teams,
    IOptions<LicensingOptions> options,
    TimeProvider clock) : IQueryHandler<GetLicensingStatusQuery, LicensingStatusDto>
{
    public async Task<LicensingStatusDto> Handle(GetLicensingStatusQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId) throw new UnauthenticatedException();
        var today = SquadAccess.Today(clock);
        var list = new List<TeamLicenseDto>();

        if (currentUser.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
        {
            foreach (var team in club.Teams)
                list.Add(await DescribeAsync(await gate.EnsureForTeamAsync(club, team.TeamId, cancellationToken), null, cancellationToken));
        }
        else if (currentUser.CoachId is { } coachId)
        {
            foreach (var squad in await squads.GetByCoachAsync(coachId, today, cancellationToken))
                if (await LicenseOfAsync(squad.TeamId, cancellationToken) is { } license)
                    list.Add(await DescribeAsync(license, squad.Id, cancellationToken));
        }
        else if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player
            && await squads.GetByPlayerAsync(player.Id, cancellationToken) is { } own
            && await LicenseOfAsync(own.TeamId, cancellationToken) is { } license)
        {
            list.Add(await DescribeAsync(license, own.Id, cancellationToken));
        }

        var o = options.Value;
        return new LicensingStatusDto(await gate.IsViewerLicensedAsync(currentUser, cancellationToken), list, o.MonthlyPriceCents, o.Currency, o.TrialDays, o.Provider);
    }

    private async Task<TeamLicense?> LicenseOfAsync(Guid teamId, CancellationToken cancellationToken)
    {
        await gate.StatusForTeamAsync(teamId, cancellationToken);
        return await licenses.GetByTeamAsync(teamId, cancellationToken);
    }

    private async Task<TeamLicenseDto> DescribeAsync(TeamLicense license, Guid? squadId, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(license.TeamId, cancellationToken);
        return LicenseMapper.ToDto(license, team?.Name ?? "Equipo", squadId, SquadAccess.Today(clock));
    }
}

internal sealed class ListClubLicensesHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    IClubStore clubs,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    TimeProvider clock) : IQueryHandler<ListClubLicensesQuery, IReadOnlyList<TeamLicenseDto>>
{
    public async Task<IReadOnlyList<TeamLicenseDto>> Handle(ListClubLicensesQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        return await ClubLicenses.ListAsync(club, gate, squads, teams, competitions, clock, cancellationToken);
    }
}

internal static class ClubLicenses
{
    public static async Task<IReadOnlyList<TeamLicenseDto>> ListAsync(
        Club club, ILicenseGate gate, ISquadStore squads, ITeamStore teams, ICompetitionStore competitions, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = SquadAccess.Today(clock);
        var result = new List<TeamLicenseDto>();
        foreach (var clubTeam in club.Teams.OrderBy(t => t.AddedAt))
        {
            var team = await teams.GetByIdAsync(clubTeam.TeamId, cancellationToken);
            if (team is null) continue;
            var license = await gate.EnsureForTeamAsync(club, team.Id, cancellationToken);
            var season = (await competitions.GetByTeamAsync(team.Id, cancellationToken))?.CurrentSeason;
            var squad = season is null ? null : await squads.GetByTeamAndSeasonAsync(team.Id, season.Id, cancellationToken);
            result.Add(LicenseMapper.ToDto(license, team.Name, squad?.Id, today));
        }
        return result;
    }
}

internal sealed class BeginLicenseCheckoutHandler(
    ICurrentUser currentUser,
    ILicenseGate gate,
    ILicenseStore licenses,
    IClubStore clubs,
    ITeamStore teams,
    ILicensePaymentGateway gateway,
    IOptions<LicensingOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<BeginLicenseCheckoutCommand, LicenseCheckoutDto>
{
    public async Task<LicenseCheckoutDto> Handle(BeginLicenseCheckoutCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        if (!club.Manages(command.TeamId))
            throw new ForbiddenException(ErrorCode.ClubTeamNotInClub);

        var team = await teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, command.TeamId);
        var license = await gate.EnsureForTeamAsync(club, team.Id, cancellationToken);

        var o = options.Value;
        var session = await gateway.BeginCheckoutAsync(club.Id, team.Id, command.Months, command.Months * o.MonthlyPriceCents, o.Currency, cancellationToken);
        var payment = LicensePayment.Begin(license, command.Months, o.MonthlyPriceCents, o.Currency, gateway.Provider, session.SessionId, clock.GetUtcNow());
        licenses.AddPayment(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return LicenseMapper.ToDto(payment, team.Name, session.Url);
    }
}

internal sealed class GetLicenseCheckoutHandler(
    ICurrentUser currentUser,
    ILicenseStore licenses,
    IClubStore clubs,
    ITeamStore teams,
    ILicensePaymentGateway gateway) : IQueryHandler<GetLicenseCheckoutQuery, LicenseCheckoutDto>
{
    public async Task<LicenseCheckoutDto> Handle(GetLicenseCheckoutQuery query, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var payment = await licenses.GetPaymentBySessionAsync(query.SessionId, cancellationToken);
        if (payment is null || payment.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.LicensePaymentNotFound, query.SessionId);
        var team = await teams.GetByIdAsync(payment.TeamId, cancellationToken);
        return LicenseMapper.ToDto(payment, team?.Name ?? "Equipo", gateway.Provider == "mock" ? $"/pago/{payment.SessionId}" : string.Empty);
    }
}

internal sealed class ConfirmLicensePaymentHandler(
    ICurrentUser currentUser,
    ILicenseStore licenses,
    IClubStore clubs,
    ITeamStore teams,
    ISquadStore squads,
    ICompetitionStore competitions,
    ILicensePaymentGateway gateway,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ConfirmLicensePaymentCommand, TeamLicenseDto>
{
    public async Task<TeamLicenseDto> Handle(ConfirmLicensePaymentCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var payment = await licenses.GetPaymentBySessionAsync(command.SessionId, cancellationToken);
        if (payment is null || payment.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.LicensePaymentNotFound, command.SessionId);
        if (payment.IsPaid)
            throw new ConflictException(ErrorCode.LicensePaymentAlreadyPaid);

        if (!await gateway.ConfirmAsync(payment.SessionId, cancellationToken))
            throw new ConflictException(ErrorCode.LicensePaymentNotConfirmed);

        var license = await licenses.GetByIdAsync(payment.LicenseId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.LicenseNotFound, payment.LicenseId);

        var now = clock.GetUtcNow();
        payment.MarkPaid(now);
        license.ExtendPaid(payment.Months, SquadAccess.Today(clock), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var team = await teams.GetByIdAsync(license.TeamId, cancellationToken);
        var season = (await competitions.GetByTeamAsync(license.TeamId, cancellationToken))?.CurrentSeason;
        var squad = season is null ? null : await squads.GetByTeamAndSeasonAsync(license.TeamId, season.Id, cancellationToken);
        return LicenseMapper.ToDto(license, team?.Name ?? "Equipo", squad?.Id, SquadAccess.Today(clock));
    }
}
