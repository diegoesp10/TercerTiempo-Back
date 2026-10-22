using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Clubs.Roster;

public sealed record UploadRosterPhotoCommand(
    [property: Description("Equipo del club.")]
    Guid TeamId,
    Guid EntryId,
    CrestUpload File) : ICommand<IReadOnlyList<RosterEntryDto>>;

internal sealed class UploadRosterPhotoValidator : AbstractValidator<UploadRosterPhotoCommand>
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp",
    };

    public UploadRosterPhotoValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).GreaterThan(0).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(MaxBytes).WithCatalogMessage(ErrorCode.RosterPhotoTooLarge);
        RuleFor(x => x.File.ContentType).Must(AllowedContentTypes.Contains)
            .WithCatalogMessage(ErrorCode.RosterPhotoFormat);
    }
}

internal sealed class UploadRosterPhotoHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    RosterStores roster,
    IPlayerPhotoStorage photos,
    IUnitOfWork unitOfWork) : ICommandHandler<UploadRosterPhotoCommand, IReadOnlyList<RosterEntryDto>>
{
    public async Task<IReadOnlyList<RosterEntryDto>> Handle(UploadRosterPhotoCommand command, CancellationToken cancellationToken)
    {
        var (seasonId, _) = await ClubRoster.ContextAsync(command.TeamId, currentUser, stores, cancellationToken);
        var entry = await ClubRoster.EntryAsync(command.TeamId, seasonId, command.EntryId, stores.Teams, cancellationToken);

        using var buffer = new MemoryStream();
        await command.File.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > UploadRosterPhotoValidator.MaxBytes)
            throw new DomainException(ErrorCode.RosterPhotoInvalid);

        var image = new CrestImage(buffer.ToArray(), command.File.ContentType.ToLowerInvariant());
        entry.SetPhoto(await photos.SaveAsync(entry.Id, image, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ClubRoster.ListAsync(command.TeamId, seasonId, roster, cancellationToken);
    }
}
