using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Teams.ImportCrest;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Clubs.ManageTeams;

public sealed record CrestUpload(string FileName, string ContentType, long Length, Stream Content);

public sealed record UploadTeamCrestCommand(
    [property: Description("Equipo del club.")]
    Guid TeamId,
    CrestUpload File) : ICommand<TeamCrestImportDto>;

internal sealed class UploadTeamCrestValidator : AbstractValidator<UploadTeamCrestCommand>
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/svg+xml", "image/gif",
    };

    public UploadTeamCrestValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.Length).GreaterThan(0).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(MaxBytes).WithCatalogMessage(ErrorCode.ClubCrestTooLarge);
        RuleFor(x => x.File.ContentType).Must(AllowedContentTypes.Contains)
            .WithCatalogMessage(ErrorCode.ClubCrestFormat);
    }
}

internal sealed class UploadTeamCrestHandler(
    ICurrentUser currentUser,
    AccountStores stores,
    ICrestStorage crests,
    IUnitOfWork unitOfWork) : ICommandHandler<UploadTeamCrestCommand, TeamCrestImportDto>
{
    public async Task<TeamCrestImportDto> Handle(UploadTeamCrestCommand command, CancellationToken cancellationToken)
    {
        var club = await Resolvers.CurrentClubAsync(currentUser, stores.Clubs, cancellationToken);
        if (!club.Manages(command.TeamId))
            throw new ForbiddenException(ErrorCode.ClubCrestOwnTeamsOnly);

        var team = await stores.Teams.GetByIdAsync(command.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, command.TeamId);

        using var buffer = new MemoryStream();
        await command.File.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > UploadTeamCrestValidator.MaxBytes)
            throw new DomainException(ErrorCode.ClubCrestInvalid);

        var image = new CrestImage(buffer.ToArray(), command.File.ContentType.ToLowerInvariant());
        team.SetCrest(await crests.SaveAsync(team.Id, image, cancellationToken), fromClub: true);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TeamCrestImportDto(team.Id, team.Name, team.CrestLink()!, image.ContentType, image.Content.Length);
    }
}
