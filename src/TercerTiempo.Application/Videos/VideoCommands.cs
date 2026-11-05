using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Passes;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Videos;

public sealed record UploadMatchVideoCommand(Guid SquadId, Guid MatchId, VideoPart Part, string? Title, VideoUpload File) : ICommand<MatchVideoDto>;

internal sealed class UploadMatchVideoValidator : AbstractValidator<UploadMatchVideoCommand>
{
    public UploadMatchVideoValidator(IOptions<VideoOptions> options)
    {
        var settings = options.Value;

        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Part).IsInEnum();
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.File).NotNull();
        RuleFor(x => x.File.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.File.ContentType)
            .Must(ct => settings.AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
            .WithCatalogMessage(ErrorCode.VideoFormatNotAllowed, string.Join(", ", settings.AllowedContentTypes));
        RuleFor(x => x.File.Length)
            .GreaterThanOrEqualTo(MatchVideo.MinSizeBytes).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(settings.MaxSizeBytes).WithCatalogMessage(ErrorCode.VideoTooLarge, settings.MaxSizeMb);
    }
}

internal sealed class UploadMatchVideoHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IVideoStorage storage,
    IVideoProcessingSignal processing,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadMatchVideoCommand, MatchVideoDto>
{
    public async Task<MatchVideoDto> Handle(UploadMatchVideoCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var uploader = await VideoManagers.CurrentAsync(currentUser, coaches, clubs, cancellationToken);

        var match = await VideoUploads.MatchOfSquadAsync(squad, command.MatchId, matches, clock, cancellationToken);

        await VideoUploads.EnsurePartAvailableAsync(squad, match.Id, command.Part, videos, cancellationToken);

        var storageKey = await storage.SaveAsync(command.File.Content, command.File.FileName, cancellationToken);

        var video = MatchVideo.Upload(
            squad.Id, squad.TeamId, match.Id, match.SeasonId, uploader.CoachId, command.Part,
            command.Title, command.File.FileName, command.File.ContentType, command.File.Length, storageKey,
            clock.GetUtcNow(), uploader.ClubId);

        videos.Add(video);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        processing.Wake();

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        return VideoMapper.ToDto(video, team?.Name ?? "Equipo", uploader.Name, VideoAccessLevel.Owner, canDownload: true, canDelete: true);
    }
}

public sealed record DeleteVideoCommand(Guid VideoId) : ICommand<bool>;

internal sealed class DeleteVideoValidator : AbstractValidator<DeleteVideoCommand>
{
    public DeleteVideoValidator() => RuleFor(x => x.VideoId).NotEmpty();
}

internal sealed class DeleteVideoHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IVideoStorage storage,
    IVideoProcessingSignal processing,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteVideoCommand, bool>
{
    public async Task<bool> Handle(DeleteVideoCommand command, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, command.VideoId);

        await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, video.SquadId, clock, cancellationToken);

        video.Delete(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Si se estaba comprimiendo, el compresor para y descarta lo suyo; el archivo que se servía se borra aquí.
        processing.Cancel(video.Id);
        await storage.DeleteAsync(video.StorageKey, cancellationToken);

        return true;
    }
}

public sealed record IssueVideoTicketCommand(Guid VideoId, bool Download = false) : ICommand<VideoTicketDto>;

internal sealed class IssueVideoTicketValidator : AbstractValidator<IssueVideoTicketCommand>
{
    public IssueVideoTicketValidator() => RuleFor(x => x.VideoId).NotEmpty();
}

internal sealed class IssueVideoTicketHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IMatchPassStore passes,
    IVideoTickets tickets,
    TimeProvider clock) : ICommandHandler<IssueVideoTicketCommand, VideoTicketDto>
{
    public async Task<VideoTicketDto> Handle(IssueVideoTicketCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        var video = await videos.GetByIdAsync(command.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, command.VideoId);

        var ownerSquad = await squads.GetByIdAsync(video.SquadId, cancellationToken);
        var pass = await PassAccess.ForAsync(currentUser, video.SquadId, video.MatchId, passes, cancellationToken);
        var clubOwner = ownerSquad is not null && await SquadAccess.IsClubOfAsync(ownerSquad, currentUser, clubs, cancellationToken);
        var level = VideoAccess.LevelFor(video, ownerSquad, currentUser, pass, clock, clubOwner);

        if (!VideoAccess.CanWatch(level))
            throw level == VideoAccessLevel.Locked ? new ForbiddenException(ErrorCode.VideoLocked) : new ForbiddenException(ErrorCode.VideoNotAllowed);

        if (command.Download)
        {
            var manager = ownerSquad is not null && (clubOwner || SquadAccess.IsStaff(ownerSquad, currentUser, clock));
            if (!VideoAccess.CanDownload(level, manager, pass))
                throw new ForbiddenException(ErrorCode.VideoDownloadDenied);
        }

        var (ticket, expiresAt) = tickets.Issue(video.Id, userId, command.Download);
        return new VideoTicketDto(video.Id, ticket, expiresAt);
    }
}
