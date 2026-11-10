using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players.Photos;

public static class PlayerPhotos
{
    public const int MaxBytes = 3 * 1024 * 1024;

    // También para las fotos de los técnicos.
    public static readonly IReadOnlySet<string> ContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    public static string Route(Guid playerId, string photoKey) => $"/api/players/{playerId}/photo?v={Version(photoKey)}";

    // La versión va en la URL para que el navegador no enseñe la foto anterior después de cambiarla.
    public static string Version(string photoKey)
    {
        var name = Path.GetFileNameWithoutExtension(photoKey);
        var dash = name.LastIndexOf('-');
        return dash >= 0 ? name[(dash + 1)..] : name;
    }

    public static string? PhotoLink(this Player player) => player.PhotoKey is null ? null : Route(player.Id, player.PhotoKey);
}

// Foto de la cuenta del jugador, subida por él mismo (el asistente del frontal recorta su figura y la manda en PNG con
// fondo transparente). Tiene prioridad sobre la foto que el club haya puesto en la ficha.
public sealed record UploadMyPhotoCommand(CrestUpload File) : ICommand<PlayerDto>;

public sealed record DeleteMyPhotoCommand : ICommand<PlayerDto>;

public sealed record OpenPlayerPhotoQuery(Guid PlayerId) : IQuery<CrestFile?>;

internal sealed class UploadMyPhotoValidator : AbstractValidator<UploadMyPhotoCommand>
{
    public UploadMyPhotoValidator()
    {
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).GreaterThan(0).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(PlayerPhotos.MaxBytes).WithCatalogMessage(ErrorCode.PlayerPhotoTooLarge);
        RuleFor(x => x.File.ContentType).Must(PlayerPhotos.ContentTypes.Contains).WithCatalogMessage(ErrorCode.RosterPhotoFormat);
    }
}

internal sealed class UploadMyPhotoHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerPhotoStorage photos,
    IUnitOfWork unitOfWork) : ICommandHandler<UploadMyPhotoCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(UploadMyPhotoCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        using var buffer = new MemoryStream();
        await command.File.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > PlayerPhotos.MaxBytes)
            throw new DomainException(ErrorCode.PlayerPhotoTooLarge);

        var image = new CrestImage(buffer.ToArray(), command.File.ContentType.ToLowerInvariant());
        player.SetPhoto(await photos.SaveAsync(player.Id, image, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken);
    }
}

internal sealed class DeleteMyPhotoHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteMyPhotoCommand, PlayerDto>
{
    public async Task<PlayerDto> Handle(DeleteMyPhotoCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        player.ClearPhoto();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken);
    }
}

internal sealed class OpenPlayerPhotoHandler(IPlayerStore players, IPlayerPhotoStorage photos) : IQueryHandler<OpenPlayerPhotoQuery, CrestFile?>
{
    public async Task<CrestFile?> Handle(OpenPlayerPhotoQuery query, CancellationToken cancellationToken)
    {
        var player = (await players.GetByIdsAsync([query.PlayerId], cancellationToken)).FirstOrDefault();
        return player?.PhotoKey is { } key ? await photos.OpenReadAsync(key, cancellationToken) : null;
    }
}
