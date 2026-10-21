using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Clubs.Appointments;
using TercerTiempo.Application.Clubs.GetMyClub;
using TercerTiempo.Application.Clubs.ManageTeams;

namespace TercerTiempo.Repository.Clubs;

internal sealed class ClubsRepository(
    IQueryHandler<GetMyClubQuery, ClubDto> getMyClub,
    ICommandHandler<AddClubTeamCommand, ClubDto> addTeam,
    ICommandHandler<RemoveClubTeamCommand, ClubDto> removeTeam,
    ICommandHandler<CreateAppointmentCommand, AppointmentDto> createAppointment,
    ICommandHandler<UpdateAppointmentCommand, AppointmentDto> updateAppointment,
    ICommandHandler<RevokeAppointmentCommand, AppointmentDto> revokeAppointment) : IClubsRepository
{
    public Task<ClubDto> GetMineAsync(CancellationToken cancellationToken = default) =>
        getMyClub.Handle(new GetMyClubQuery(), cancellationToken);

    public Task<ClubDto> AddTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        addTeam.Handle(new AddClubTeamCommand(teamId), cancellationToken);

    public Task<ClubDto> RemoveTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        removeTeam.Handle(new RemoveClubTeamCommand(teamId), cancellationToken);

    public Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default) =>
        createAppointment.Handle(request.ToCommand(), cancellationToken);

    public Task<AppointmentDto> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken cancellationToken = default) =>
        updateAppointment.Handle(new UpdateAppointmentCommand(appointmentId, request.ValidUntil), cancellationToken);

    public Task<AppointmentDto> RevokeAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        revokeAppointment.Handle(new RevokeAppointmentCommand(appointmentId), cancellationToken);
}
