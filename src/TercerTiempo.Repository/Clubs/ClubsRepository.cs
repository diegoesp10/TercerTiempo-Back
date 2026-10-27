using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Clubs.Appointments;
using TercerTiempo.Application.Clubs.Catalog;
using TercerTiempo.Application.Clubs.GetMyClub;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Clubs.Roster;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Teams.ImportCrest;

namespace TercerTiempo.Repository.Clubs;

internal sealed class ClubsRepository(
    IQueryHandler<GetClubCatalogQuery, IReadOnlyList<ClubCatalogDto>> getCatalog,
    IQueryHandler<GetMyClubQuery, ClubDto> getMyClub,
    ICommandHandler<AddClubTeamCommand, ClubDto> addTeam,
    ICommandHandler<RemoveClubTeamCommand, ClubDto> removeTeam,
    ICommandHandler<OpenClubSquadCommand, ClubSquadDto> openSquad,
    ICommandHandler<UploadTeamCrestCommand, TeamCrestImportDto> uploadCrest,
    ICommandHandler<AddClubRosterEntryCommand, IReadOnlyList<RosterEntryDto>> addRosterEntry,
    ICommandHandler<UpdateClubRosterEntryCommand, IReadOnlyList<RosterEntryDto>> updateRosterEntry,
    ICommandHandler<RemoveClubRosterEntryCommand, IReadOnlyList<RosterEntryDto>> removeRosterEntry,
    IQueryHandler<GetClubRosterEntryQuery, RosterEntryDetailDto> getRosterEntry,
    ICommandHandler<UploadRosterPhotoCommand, IReadOnlyList<RosterEntryDto>> uploadRosterPhoto,
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

    public Task<ClubSquadDto> OpenSquadAsync(Guid teamId, OpenClubSquadRequest request, CancellationToken cancellationToken = default) =>
        openSquad.Handle(new OpenClubSquadCommand(teamId, request.MatchId), cancellationToken);

    public Task<TeamCrestImportDto> UploadTeamCrestAsync(Guid teamId, CrestUpload file, CancellationToken cancellationToken = default) =>
        uploadCrest.Handle(new UploadTeamCrestCommand(teamId, file), cancellationToken);

    public Task<IReadOnlyList<RosterEntryDto>> AddRosterEntryAsync(Guid teamId, RosterEntryRequest request, CancellationToken cancellationToken = default) =>
        addRosterEntry.Handle(new AddClubRosterEntryCommand(teamId, request.FullName, request.Position, request.ShirtNumber), cancellationToken);

    public Task<IReadOnlyList<RosterEntryDto>> UpdateRosterEntryAsync(Guid teamId, Guid entryId, RosterEntryRequest request, CancellationToken cancellationToken = default) =>
        updateRosterEntry.Handle(new UpdateClubRosterEntryCommand(teamId, entryId, request.FullName, request.Position, request.ShirtNumber), cancellationToken);

    public Task<IReadOnlyList<RosterEntryDto>> RemoveRosterEntryAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default) =>
        removeRosterEntry.Handle(new RemoveClubRosterEntryCommand(teamId, entryId), cancellationToken);

    public Task<RosterEntryDetailDto> GetRosterEntryAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default) =>
        getRosterEntry.Handle(new GetClubRosterEntryQuery(teamId, entryId), cancellationToken);

    public Task<IReadOnlyList<RosterEntryDto>> UploadRosterPhotoAsync(Guid teamId, Guid entryId, CrestUpload file, CancellationToken cancellationToken = default) =>
        uploadRosterPhoto.Handle(new UploadRosterPhotoCommand(teamId, entryId, file), cancellationToken);

    public Task<AppointmentDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default) =>
        createAppointment.Handle(request.ToCommand(), cancellationToken);

    public Task<AppointmentDto> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken cancellationToken = default) =>
        updateAppointment.Handle(new UpdateAppointmentCommand(appointmentId, request.ValidUntil), cancellationToken);

    public Task<AppointmentDto> RevokeAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        revokeAppointment.Handle(new RevokeAppointmentCommand(appointmentId), cancellationToken);
}
