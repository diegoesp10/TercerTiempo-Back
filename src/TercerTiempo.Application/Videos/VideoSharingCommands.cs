using System.ComponentModel;
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

public sealed record VideoShareLinkDto(
    Guid Id,
    Guid VideoId,
    [property: Description("Token que va en la URL pública del visor (/ver/{token}).")]
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool Active);

public sealed record SharedVideoDto(
    Guid VideoId,
    string Title,
    VideoPart Part,
    string TeamName,
    MatchDto Match,
    string ContentType,
    long SizeBytes,
    DateTimeOffset ExpiresAt);

public sealed record SetVideoVisibilityCommand(
    Guid VideoId,
    [property: Description("Private (plantilla y compradores) o Public (cualquiera que vea el partido, sin pagar).")]
    VideoVisibility Visibility) : ICommand<MatchVideoDto>;

public sealed record CreateVideoShareLinkCommand(
    Guid VideoId,
    [property: Description("Horas de validez del enlace; por defecto 24 y como máximo Videos:MaxShareHours.")]
    int? ExpiresInHours) : ICommand<VideoShareLinkDto>;

public sealed record ListVideoShareLinksQuery(Guid VideoId) : IQuery<IReadOnlyList<VideoShareLinkDto>>;

public sealed record RevokeVideoShareLinkCommand(Guid VideoId, Guid LinkId) : ICommand<IReadOnlyList<VideoShareLinkDto>>;

public sealed record GetSharedVideoQuery(string Token) : IQuery<SharedVideoDto>;

public sealed record OpenSharedVideoStreamQuery(string Token) : IQuery<VideoStreamDto>;

internal sealed class SetVideoVisibilityValidator : AbstractValidator<SetVideoVisibilityCommand>
{
    public SetVideoVisibilityValidator()
    {
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

internal sealed class CreateVideoShareLinkValidator : AbstractValidator<CreateVideoShareLinkCommand>
{
    public CreateVideoShareLinkValidator(IOptions<VideoOptions> options)
    {
        RuleFor(x => x.VideoId).NotEmpty();
        RuleFor(x => x.ExpiresInHours).InclusiveBetween(1, options.Value.MaxShareHours).When(x => x.ExpiresInHours.HasValue)
            .WithCatalogMessage(ErrorCode.VideoShareHoursRange, options.Value.MaxShareHours);
    }
}

internal static class VideoSharing
{
    public static async Task<MatchVideo> OwnedVideoAsync(
        Guid videoId, ICurrentUser currentUser, IMatchVideoStore videos, ISquadStore squads, IClubStore clubs, TimeProvider clock, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, videoId);

        await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, video.SquadId, clock, cancellationToken);
        return video;
    }

    public static VideoShareLinkDto ToDto(VideoShareLink link, DateTimeOffset now) =>
        new(link.Id, link.VideoId, link.Token, link.CreatedAt, link.ExpiresAt, link.IsValid(now));

    public static async Task<IReadOnlyList<VideoShareLinkDto>> ListAsync(Guid videoId, IVideoShareStore shares, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return (await shares.GetByVideoAsync(videoId, cancellationToken))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => ToDto(l, now))
            .ToList();
    }

    public static async Task<(VideoShareLink Link, MatchVideo Video)> ValidLinkAsync(
        string token, IVideoShareStore shares, IMatchVideoStore videos, TimeProvider clock, CancellationToken cancellationToken)
    {
        var link = token.Length is >= 32 and <= 100 ? await shares.GetByTokenAsync(token, cancellationToken) : null;
        if (link is null || !link.IsValid(clock.GetUtcNow()))
            throw new ForbiddenException(ErrorCode.VideoShareInvalid);

        var video = await videos.GetByIdAsync(link.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, link.VideoId);

        return (link, video);
    }
}

internal sealed class SetVideoVisibilityHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    ITeamStore teams,
    ICoachStore coaches,
    IMatchVideoStore videos,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetVideoVisibilityCommand, MatchVideoDto>
{
    public async Task<MatchVideoDto> Handle(SetVideoVisibilityCommand command, CancellationToken cancellationToken)
    {
        var video = await VideoSharing.OwnedVideoAsync(command.VideoId, currentUser, videos, squads, clubs, clock, cancellationToken);

        video.SetVisibility(command.Visibility);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var team = await teams.GetByIdAsync(video.TeamId, cancellationToken);
        var uploader = await VideoManagers.UploaderNameAsync(video, coaches, clubs, cancellationToken);
        return VideoMapper.ToDto(video, team?.Name ?? "Equipo", uploader, VideoAccessLevel.Owner, canDelete: true);
    }
}

internal sealed class CreateVideoShareLinkHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IVideoShareStore shares,
    IOptions<VideoOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateVideoShareLinkCommand, VideoShareLinkDto>
{
    public async Task<VideoShareLinkDto> Handle(CreateVideoShareLinkCommand command, CancellationToken cancellationToken)
    {
        var video = await VideoSharing.OwnedVideoAsync(command.VideoId, currentUser, videos, squads, clubs, clock, cancellationToken);

        var now = clock.GetUtcNow();
        var hours = command.ExpiresInHours ?? options.Value.DefaultShareHours;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var link = VideoShareLink.Create(video.Id, currentUser.CoachId, token, now, TimeSpan.FromHours(hours), currentUser.ClubId);

        shares.Add(link);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VideoSharing.ToDto(link, now);
    }
}

internal sealed class ListVideoShareLinksHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IVideoShareStore shares,
    TimeProvider clock) : IQueryHandler<ListVideoShareLinksQuery, IReadOnlyList<VideoShareLinkDto>>
{
    public async Task<IReadOnlyList<VideoShareLinkDto>> Handle(ListVideoShareLinksQuery query, CancellationToken cancellationToken)
    {
        var video = await VideoSharing.OwnedVideoAsync(query.VideoId, currentUser, videos, squads, clubs, clock, cancellationToken);
        return await VideoSharing.ListAsync(video.Id, shares, clock, cancellationToken);
    }
}

internal sealed class RevokeVideoShareLinkHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    IMatchVideoStore videos,
    IVideoShareStore shares,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RevokeVideoShareLinkCommand, IReadOnlyList<VideoShareLinkDto>>
{
    public async Task<IReadOnlyList<VideoShareLinkDto>> Handle(RevokeVideoShareLinkCommand command, CancellationToken cancellationToken)
    {
        var video = await VideoSharing.OwnedVideoAsync(command.VideoId, currentUser, videos, squads, clubs, clock, cancellationToken);

        var link = (await shares.GetByVideoAsync(video.Id, cancellationToken)).FirstOrDefault(l => l.Id == command.LinkId)
            ?? throw new NotFoundException(ErrorCode.VideoShareNotFound, command.LinkId);

        link.Revoke(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await VideoSharing.ListAsync(video.Id, shares, clock, cancellationToken);
    }
}

internal sealed class GetSharedVideoHandler(
    IVideoShareStore shares,
    IMatchVideoStore videos,
    IMatchStore matches,
    ITeamStore teams,
    TimeProvider clock) : IQueryHandler<GetSharedVideoQuery, SharedVideoDto>
{
    public async Task<SharedVideoDto> Handle(GetSharedVideoQuery query, CancellationToken cancellationToken)
    {
        var (link, video) = await VideoSharing.ValidLinkAsync(query.Token, shares, videos, clock, cancellationToken);

        var match = await matches.GetByIdAsync(video.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, video.MatchId);
        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);

        return new SharedVideoDto(
            video.Id, video.Title, video.Part, teamMap.GetValueOrDefault(video.TeamId)?.Name ?? "Equipo",
            MatchMapper.ToDto(match, teamMap), video.ContentType, video.SizeBytes, link.ExpiresAt);
    }
}

internal sealed class OpenSharedVideoStreamHandler(
    IVideoShareStore shares,
    IMatchVideoStore videos,
    IVideoStorage storage,
    TimeProvider clock) : IQueryHandler<OpenSharedVideoStreamQuery, VideoStreamDto>
{
    public async Task<VideoStreamDto> Handle(OpenSharedVideoStreamQuery query, CancellationToken cancellationToken)
    {
        var (_, video) = await VideoSharing.ValidLinkAsync(query.Token, shares, videos, clock, cancellationToken);
        var content = await storage.OpenReadAsync(video.StorageKey, cancellationToken);
        return new VideoStreamDto(content, video.ContentType, video.FileName, video.SizeBytes);
    }
}
