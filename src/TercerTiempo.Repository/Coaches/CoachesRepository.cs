using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Application.Coaches.Photos;
using TercerTiempo.Application.Coaches.Profile;

namespace TercerTiempo.Repository.Coaches;

public interface ICoachesRepository
{
    Task<CoachDto> RedeemAppointmentAsync(RedeemAppointmentCommand command, CancellationToken cancellationToken = default);
    Task<CoachProfileDto> GetProfileAsync(Guid coachId, CancellationToken cancellationToken = default);
    Task<CoachDto> UploadPhotoAsync(CrestUpload file, CancellationToken cancellationToken = default);
    Task<CoachDto> DeletePhotoAsync(CancellationToken cancellationToken = default);
    Task<CrestFile?> OpenPhotoAsync(Guid coachId, CancellationToken cancellationToken = default);
}

internal sealed class CoachesRepository(
    ICommandHandler<RedeemAppointmentCommand, CoachDto> redeem,
    IQueryHandler<GetCoachProfileQuery, CoachProfileDto> profile,
    ICommandHandler<UploadMyCoachPhotoCommand, CoachDto> uploadPhoto,
    ICommandHandler<DeleteMyCoachPhotoCommand, CoachDto> deletePhoto,
    IQueryHandler<OpenCoachPhotoQuery, CrestFile?> openPhoto) : ICoachesRepository
{
    public Task<CoachDto> RedeemAppointmentAsync(RedeemAppointmentCommand command, CancellationToken cancellationToken = default) =>
        redeem.Handle(command, cancellationToken);

    public Task<CoachProfileDto> GetProfileAsync(Guid coachId, CancellationToken cancellationToken = default) =>
        profile.Handle(new GetCoachProfileQuery(coachId), cancellationToken);

    public Task<CoachDto> UploadPhotoAsync(CrestUpload file, CancellationToken cancellationToken = default) =>
        uploadPhoto.Handle(new UploadMyCoachPhotoCommand(file), cancellationToken);

    public Task<CoachDto> DeletePhotoAsync(CancellationToken cancellationToken = default) =>
        deletePhoto.Handle(new DeleteMyCoachPhotoCommand(), cancellationToken);

    public Task<CrestFile?> OpenPhotoAsync(Guid coachId, CancellationToken cancellationToken = default) =>
        openPhoto.Handle(new OpenCoachPhotoQuery(coachId), cancellationToken);
}
