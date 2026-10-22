using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Clubs.Appointments;

public sealed record CreateAppointmentCommand(
    [property: Description("Equipo del club.")]
    Guid TeamId,
    [property: Description("Nombre del técnico, tal y como lo conoce el club.")]
    string CoachName,
    [property: Description("Email del técnico (opcional, orientativo).")]
    string? Email,
    [property: Description("Rol: HeadCoach, AssistantCoach, Delegate, Physio, Analyst u Other.")]
    StaffRole Role,
    [property: Description("Último día de validez (yyyy-MM-dd). Vacío = hasta el final de la temporada.")]
    DateOnly? ValidUntil) : ICommand<AppointmentDto>;

internal sealed class CreateAppointmentValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.CoachName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Role).IsInEnum();
    }
}

internal sealed class CreateAppointmentHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateAppointmentCommand, AppointmentDto>
{
    public async Task<AppointmentDto> Handle(CreateAppointmentCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);

        if (!club.Manages(command.TeamId))
            throw new ForbiddenException(ErrorCode.ClubTeamNotLinked);

        var (_, season) = await Resolvers.SeasonOfTeamAsync(stores.Competitions, command.TeamId, cancellationToken);
        var today = SquadAccess.Today(stores.Clock);
        var validUntil = command.ValidUntil ?? season.EndsOn;

        if (validUntil > season.EndsOn)
            throw new ConflictException(ErrorCode.AppointmentBeyondSeason, season.EndsOn.ToString("dd/MM/yyyy"));

        if (validUntil < today)
            throw new ConflictException(ErrorCode.AppointmentValidityPast);

        var appointment = CoachAppointment.Create(
            club.Id, command.TeamId, season.Id, command.CoachName, command.Email, command.Role, today, validUntil, stores.Clock.GetUtcNow());

        stores.Appointments.Add(appointment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubMapper.ToDtoAsync(appointment, stores, today, cancellationToken);
    }
}

public sealed record RevokeAppointmentCommand(Guid AppointmentId) : ICommand<AppointmentDto>;

internal sealed class RevokeAppointmentValidator : AbstractValidator<RevokeAppointmentCommand>
{
    public RevokeAppointmentValidator() => RuleFor(x => x.AppointmentId).NotEmpty();
}

internal sealed class RevokeAppointmentHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<RevokeAppointmentCommand, AppointmentDto>
{
    public async Task<AppointmentDto> Handle(RevokeAppointmentCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        var appointment = await AppointmentOfClubAsync(stores, club.Id, command.AppointmentId, cancellationToken);
        var now = stores.Clock.GetUtcNow();

        appointment.Revoke(now);

        if (appointment.CoachId is { } coachId
            && await stores.Squads.GetByTeamAndSeasonAsync(appointment.TeamId, appointment.SeasonId, cancellationToken) is { } squad
            && squad.Staff.Any(s => s.CoachId == coachId && s.IsActive))
        {
            squad.RemoveStaff(coachId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubMapper.ToDtoAsync(appointment, stores, SquadAccess.Today(stores.Clock), cancellationToken);
    }

    internal static async Task<CoachAppointment> AppointmentOfClubAsync(AccountStores stores, Guid clubId, Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await stores.Appointments.GetByIdAsync(appointmentId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.AppointmentNotFound, appointmentId);

        if (appointment.ClubId != clubId)
            throw new ForbiddenException(ErrorCode.AppointmentNotOfClub);

        return appointment;
    }
}

public sealed record UpdateAppointmentCommand(
    Guid AppointmentId,
    [property: Description("Nuevo último día de validez (yyyy-MM-dd).")]
    DateOnly ValidUntil) : ICommand<AppointmentDto>;

internal sealed class UpdateAppointmentValidator : AbstractValidator<UpdateAppointmentCommand>
{
    public UpdateAppointmentValidator() => RuleFor(x => x.AppointmentId).NotEmpty();
}

internal sealed class UpdateAppointmentHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateAppointmentCommand, AppointmentDto>
{
    public async Task<AppointmentDto> Handle(UpdateAppointmentCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        var appointment = await RevokeAppointmentHandler.AppointmentOfClubAsync(stores, club.Id, command.AppointmentId, cancellationToken);
        var (_, season) = await Resolvers.SeasonByIdAsync(stores.Competitions, appointment.SeasonId, cancellationToken);

        if (command.ValidUntil > season.EndsOn)
            throw new ConflictException(ErrorCode.AppointmentBeyondSeason, season.EndsOn.ToString("dd/MM/yyyy"));

        appointment.SetValidity(command.ValidUntil);

        if (appointment.CoachId is { } coachId
            && await stores.Squads.GetByTeamAndSeasonAsync(appointment.TeamId, appointment.SeasonId, cancellationToken) is { } squad
            && squad.Staff.Any(s => s.CoachId == coachId && s.IsActive))
        {
            squad.SetStaffValidity(coachId, command.ValidUntil);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubMapper.ToDtoAsync(appointment, stores, SquadAccess.Today(stores.Clock), cancellationToken);
    }
}
