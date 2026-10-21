using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Clubs.Appointments;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Clubs.GetMyClub;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Teams.ImportCrest;

namespace TercerTiempo.Repository.Clubs;

internal sealed class ClubsRepository(
    IQueryHandler<GetClubCatalogQuery, IReadOnlyList<ClubCatalogDto>> getCatalog,
    IQueryHandler<GetMyClubQuery, ClubDto> getMyClub,
    ICommandHandler<AddClubTeamCommand, ClubDto> addTeam,
    ICommandHandler<RemoveClubTeamCommand, ClubDto> removeTeam,
    ICommandHandler<UploadTeamCrestCommand, TeamCrestImportDto> uploadCrest,
    ICommandHandler<CreateAppointmentCommand, AppointmentDto> createAppointment,
    ICommandHandler<UpdateAppointmentCommand, AppointmentDto> updateAppointment,
    ICommandHandler<RevokeAppointmentCommand, AppointmentDto> revokeAppointment) : IClubsRepository
{
    public Task<IReadOnlyList<ClubCatalogDto>> GetCatalogAsync(string? search, CancellationToken cancellationToken = default) =>
        getCatalog.Handle(new GetClubCatalogQuery(search), cancellationToken);

    public Task<ClubDto> GetMineAsync(CancellationToken cancellationToken = default) =>
        getMyClub.Handle(new GetMyClubQuery(), cancellationToken);

    public Task<ClubDto> AddTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        addTeam.Handle(new AddClubTeamCommand(teamId), cancellationToken);

    public Task<ClubDto> RemoveTeamAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        removeTeam.Handle(new RemoveClubTeamCommand(teamId), cancellationToken);

    public Task<TeamCrestImportDto> UploadTeamCrestAsync(Guid teamId, CrestUpload file, CancellationToken cancellationToken = default) =>
        uploadCrest.Handle(new UploadTeamCrestCommand(teamId, file), cancellationToken);

    public Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default) =>
        createAppointment.Handle(request.ToCommand(), cancellationToken);

    public Task<AppointmentDto> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken cancellationToken = default) =>
        updateAppointment.Handle(new UpdateAppointmentCommand(appointmentId, request.ValidUntil), cancellationToken);

    public Task<AppointmentDto> RevokeAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        revokeAppointment.Handle(new RevokeAppointmentCommand(appointmentId), cancellationToken);
}
