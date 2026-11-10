using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players.Photos;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Coaches.Photos;

public static class CoachPhotos
{
    public static string? PhotoLink(this Coach coach) =>
        coach.PhotoKey is null ? null : $"/api/coaches/{coach.Id}/photo?v={PlayerPhotos.Version(coach.PhotoKey)}";
}

// Foto del técnico, como la del jugador: la sube él mismo desde su perfil (el asistente del frontal recorta su figura y la
// manda en PNG con fondo transparente) y solo él la cambia o la quita. Va en la carpeta de fotos de los jugadores: la clave
// lleva el id de su dueño.
public sealed record UploadMyCoachPhotoCommand(CrestUpload File) : ICommand<CoachDto>;

public sealed record DeleteMyCoachPhotoCommand : ICommand<CoachDto>;

public sealed record OpenCoachPhotoQuery(Guid CoachId) : IQuery<CrestFile?>;

internal sealed class UploadMyCoachPhotoValidator : AbstractValidator<UploadMyCoachPhotoCommand>
{
    public UploadMyCoachPhotoValidator()
    {
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).GreaterThan(0).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(PlayerPhotos.MaxBytes).WithCatalogMessage(ErrorCode.PlayerPhotoTooLarge);
        RuleFor(x => x.File.ContentType).Must(PlayerPhotos.ContentTypes.Contains).WithCatalogMessage(ErrorCode.RosterPhotoFormat);
    }
}

internal sealed class UploadMyCoachPhotoHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IPlayerPhotoStorage photos,
    IUnitOfWork unitOfWork) : ICommandHandler<UploadMyCoachPhotoCommand, CoachDto>
{
    public async Task<CoachDto> Handle(UploadMyCoachPhotoCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, stores.Coaches, cancellationToken);

        using var buffer = new MemoryStream();
        await command.File.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > PlayerPhotos.MaxBytes)
            throw new DomainException(ErrorCode.PlayerPhotoTooLarge);

        var image = new CrestImage(buffer.ToArray(), command.File.ContentType.ToLowerInvariant());
        coach.SetPhoto(await photos.SaveAsync(coach.Id, image, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await CoachMapper.ToDtoAsync(coach, stores, cancellationToken);
    }
}

internal sealed class DeleteMyCoachPhotoHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteMyCoachPhotoCommand, CoachDto>
{
    public async Task<CoachDto> Handle(DeleteMyCoachPhotoCommand command, CancellationToken cancellationToken)
    {
        var coach = await Resolvers.CurrentCoachAsync(currentUser, stores.Coaches, cancellationToken);
        coach.ClearPhoto();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await CoachMapper.ToDtoAsync(coach, stores, cancellationToken);
    }
}

internal sealed class OpenCoachPhotoHandler(ICoachStore coaches, IPlayerPhotoStorage photos) : IQueryHandler<OpenCoachPhotoQuery, CrestFile?>
{
    public async Task<CrestFile?> Handle(OpenCoachPhotoQuery query, CancellationToken cancellationToken)
    {
        var coach = await coaches.GetByIdAsync(query.CoachId, cancellationToken);
        return coach?.PhotoKey is { } key ? await photos.OpenReadAsync(key, cancellationToken) : null;
    }
}
