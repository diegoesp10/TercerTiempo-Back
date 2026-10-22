using System.ComponentModel;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

public sealed record VideoUploadDto(
    Guid UploadId,
    Guid SquadId,
    Guid MatchId,
    VideoPart Part,
    string FileName,
    long SizeBytes,
    int ChunkSize,
    int TotalChunks,
    long ReceivedBytes,
    [property: Description("Índice del siguiente trozo que espera la API (los trozos van en orden).")]
    int NextChunk,
    bool Complete,
    DateTimeOffset ExpiresAt);

public sealed record BeginVideoUploadCommand(
    Guid SquadId,
    Guid MatchId,
    [property: Description("Full (partido completo), FirstHalf o SecondHalf.")]
    VideoPart Part,
    string? Title,
    string FileName,
    [property: Description("video/mp4 o video/webm.")]
    string ContentType,
    long SizeBytes) : ICommand<VideoUploadDto>;

public sealed record GetVideoUploadQuery(Guid SquadId, Guid UploadId) : IQuery<VideoUploadDto>;

public sealed record AppendVideoChunkCommand(Guid SquadId, Guid UploadId, int ChunkIndex, long Length, Stream Content) : ICommand<VideoUploadDto>;

public sealed record CompleteVideoUploadCommand(Guid SquadId, Guid UploadId) : ICommand<MatchVideoDto>;

public sealed record CancelVideoUploadCommand(Guid SquadId, Guid UploadId) : ICommand<bool>;

internal sealed class BeginVideoUploadValidator : AbstractValidator<BeginVideoUploadCommand>
{
    public BeginVideoUploadValidator(IOptions<VideoOptions> options)
    {
        var settings = options.Value;

        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Part).IsInEnum();
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType)
            .Must(ct => settings.AllowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
            .WithCatalogMessage(ErrorCode.VideoFormatNotAllowed, string.Join(", ", settings.AllowedContentTypes));
        RuleFor(x => x.SizeBytes)
            .GreaterThanOrEqualTo(MatchVideo.MinSizeBytes).WithCatalogMessage(ErrorCode.FileEmpty)
            .LessThanOrEqualTo(settings.MaxSizeBytes).WithCatalogMessage(ErrorCode.VideoTooLarge, settings.MaxSizeMb);
    }
}

internal sealed class AppendVideoChunkValidator : AbstractValidator<AppendVideoChunkCommand>
{
    public AppendVideoChunkValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.UploadId).NotEmpty();
        RuleFor(x => x.ChunkIndex).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Length).GreaterThan(0);
        RuleFor(x => x.Content).NotNull();
    }
}

internal static class VideoUploads
{
    public static async Task<VideoUploadSession> SessionAsync(
        Guid squadId, Guid uploadId, ICurrentUser currentUser, IVideoUploadStore uploads,
        ISquadStore squads, IClubStore clubs, TimeProvider clock, CancellationToken cancellationToken, bool allowExpired = false)
    {
        await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, squadId, clock, cancellationToken);
        var session = await uploads.GetByIdAsync(uploadId, cancellationToken);
        if (session is null || session.SquadId != squadId
            || !((session.CoachId != null && session.CoachId == currentUser.CoachId)
                || (session.ClubId != null && session.ClubId == currentUser.ClubId)))
            throw new NotFoundException(ErrorCode.VideoUploadNotFound, uploadId);
        if (!allowExpired && session.IsExpired(clock.GetUtcNow())) throw new DomainException(ErrorCode.VideoUploadExpired);
        return session;
    }

    public static VideoUploadDto ToDto(VideoUploadSession s) =>
        new(s.Id, s.SquadId, s.MatchId, s.Part, s.FileName, s.SizeBytes, s.ChunkSize, s.TotalChunks, s.ReceivedBytes, s.NextChunk, s.IsComplete, s.ExpiresAt);

    public static async Task<Match> MatchOfSquadAsync(Squad squad, Guid matchId, IMatchStore matches, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(matchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, matchId);

        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.MatchNotOfSquad);

        return match;
    }

    public static async Task EnsurePartAvailableAsync(Squad squad, Guid matchId, VideoPart part, IMatchVideoStore videos, CancellationToken cancellationToken)
    {
        var existing = (await videos.GetByMatchAsync(matchId, cancellationToken)).Where(v => v.SquadId == squad.Id);
        MatchVideo.EnsurePartAvailable(existing, part);
    }

    // Las sesiones caducadas del técnico se limpian al empezar otra, sin un trabajo aparte.
    public static async Task PurgeExpiredAsync(Guid? coachId, Guid? clubId, IVideoUploadStore uploads, IVideoStorage storage, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var stale in (await uploads.GetByOwnerAsync(coachId, clubId, cancellationToken)).Where(s => s.IsExpired(now)))
        {
            await storage.DiscardUploadAsync(stale.StorageKey, cancellationToken);
            uploads.Remove(stale);
        }
    }
}

internal sealed class BeginVideoUploadHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    IMatchVideoStore videos,
    IVideoUploadStore uploads,
    IVideoStorage storage,
    IOptions<VideoOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<BeginVideoUploadCommand, VideoUploadDto>
{
    public async Task<VideoUploadDto> Handle(BeginVideoUploadCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var uploader = await VideoManagers.CurrentAsync(currentUser, coaches, clubs, cancellationToken);
        var match = await VideoUploads.MatchOfSquadAsync(squad, command.MatchId, matches, cancellationToken);
        await VideoUploads.EnsurePartAvailableAsync(squad, match.Id, command.Part, videos, cancellationToken);
        await VideoUploads.PurgeExpiredAsync(uploader.CoachId, uploader.ClubId, uploads, storage, clock, cancellationToken);

        var settings = options.Value;
        var storageKey = await storage.BeginUploadAsync(command.FileName, cancellationToken);
        var session = VideoUploadSession.Begin(
            squad.Id, match.Id, uploader.CoachId, command.Part, command.Title, command.FileName, command.ContentType.ToLowerInvariant(),
            command.SizeBytes, settings.ChunkSizeBytes, storageKey, clock.GetUtcNow(), TimeSpan.FromHours(settings.UploadExpiryHours), uploader.ClubId);

        uploads.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VideoUploads.ToDto(session);
    }
}

internal sealed class GetVideoUploadHandler(ICurrentUser currentUser, IVideoUploadStore uploads, ISquadStore squads, IClubStore clubs, TimeProvider clock)
    : IQueryHandler<GetVideoUploadQuery, VideoUploadDto>
{
    public async Task<VideoUploadDto> Handle(GetVideoUploadQuery query, CancellationToken cancellationToken) =>
        VideoUploads.ToDto(await VideoUploads.SessionAsync(query.SquadId, query.UploadId, currentUser, uploads, squads, clubs, clock, cancellationToken));
}

internal sealed class AppendVideoChunkHandler(
    ICurrentUser currentUser,
    IVideoUploadStore uploads,
    ISquadStore squads,
    IClubStore clubs,
    IVideoStorage storage,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<AppendVideoChunkCommand, VideoUploadDto>
{
    public async Task<VideoUploadDto> Handle(AppendVideoChunkCommand command, CancellationToken cancellationToken)
    {
        var session = await VideoUploads.SessionAsync(command.SquadId, command.UploadId, currentUser, uploads, squads, clubs, clock, cancellationToken);
        var now = clock.GetUtcNow();
        session.EnsureAccepts(command.ChunkIndex, command.Length, now);

        var written = await storage.AppendUploadAsync(session.StorageKey, command.Content, session.ReceivedBytes, command.Length, cancellationToken);
        if (written != command.Length)
            throw new DomainException(ErrorCode.VideoUploadChunkSize, command.Length);

        session.Received(written, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VideoUploads.ToDto(session);
    }
}

internal sealed class CompleteVideoUploadHandler(
    ICurrentUser currentUser,
    ICoachStore coaches,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IMatchVideoStore videos,
    IVideoUploadStore uploads,
    IVideoStorage storage,
    IOptions<VideoOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CompleteVideoUploadCommand, MatchVideoDto>
{
    public async Task<MatchVideoDto> Handle(CompleteVideoUploadCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var uploader = await VideoManagers.CurrentAsync(currentUser, coaches, clubs, cancellationToken);
        var session = await VideoUploads.SessionAsync(command.SquadId, command.UploadId, currentUser, uploads, squads, clubs, clock, cancellationToken);
        session.EnsureComplete();

        var match = await VideoUploads.MatchOfSquadAsync(squad, session.MatchId, matches, cancellationToken);
        await VideoUploads.EnsurePartAvailableAsync(squad, match.Id, session.Part, videos, cancellationToken);

        var storageKey = await storage.CommitUploadAsync(session.StorageKey, session.FileName, cancellationToken);

        var video = MatchVideo.Upload(
            squad.Id, squad.TeamId, match.Id, match.SeasonId, uploader.CoachId, session.Part,
            session.Title, session.FileName, session.ContentType, session.SizeBytes, storageKey,
            options.Value.DefaultPriceCents, clock.GetUtcNow(), uploader.ClubId);

        videos.Add(video);
        uploads.Remove(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        return VideoMapper.ToDto(video, team?.Name ?? "Equipo", uploader.Name, VideoAccessLevel.Owner, canDelete: true);
    }
}

internal sealed class CancelVideoUploadHandler(
    ICurrentUser currentUser,
    IVideoUploadStore uploads,
    ISquadStore squads,
    IClubStore clubs,
    TimeProvider clock,
    IVideoStorage storage,
    IUnitOfWork unitOfWork) : ICommandHandler<CancelVideoUploadCommand, bool>
{
    public async Task<bool> Handle(CancelVideoUploadCommand command, CancellationToken cancellationToken)
    {
        var session = await VideoUploads.SessionAsync(command.SquadId, command.UploadId, currentUser, uploads, squads, clubs, clock, cancellationToken, allowExpired: true);

        await storage.DiscardUploadAsync(session.StorageKey, cancellationToken);
        uploads.Remove(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
