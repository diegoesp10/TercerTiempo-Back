using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Videos;

/// <summary>Vídeos subidos para un partido (por cualquiera de los dos equipos) con el acceso del usuario a cada uno.</summary>
public sealed record ListMatchVideosQuery(Guid MatchId) : IQuery<IReadOnlyList<MatchVideoDto>>;

internal sealed class ListMatchVideosHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ISquadStore squads,
    ITeamStore teams,
    ICoachStore coaches,
    IMatchVideoStore videos,
    IVideoPurchaseStore purchases,
    TimeProvider clock) : IQueryHandler<ListMatchVideosQuery, IReadOnlyList<MatchVideoDto>>
{
    public async Task<IReadOnlyList<MatchVideoDto>> Handle(ListMatchVideosQuery query, CancellationToken cancellationToken)
    {
        _ = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var list = await videos.GetByMatchAsync(query.MatchId, cancellationToken);
        if (list.Count == 0) return [];

        var squadMap = new Dictionary<Guid, Squad>();
        foreach (var squadId in list.Select(v => v.SquadId).Distinct())
        {
            if (await squads.GetByIdAsync(squadId, cancellationToken) is { } squad)
                squadMap[squadId] = squad;
        }

        var teamMap = (await teams.GetByIdsAsync(list.Select(v => v.TeamId), cancellationToken)).ToDictionary(t => t.Id);
        var coachMap = (await coaches.GetByIdsAsync(list.Select(v => v.UploadedByCoachId), cancellationToken)).ToDictionary(c => c.Id);

        var purchasedIds = currentUser.CoachId is { } coachId
            ? (await purchases.GetByCoachAsync(coachId, cancellationToken)).Select(p => p.VideoId).ToHashSet()
            : [];

        return list
            .OrderBy(v => v.UploadedAt)
            .Select(v =>
            {
                var ownerSquad = squadMap.GetValueOrDefault(v.SquadId);
                var level = VideoAccess.LevelFor(v, ownerSquad, currentUser, purchasedIds.Contains(v.Id), clock);
                var canDelete = ownerSquad is not null && SquadAccess.IsStaff(ownerSquad, currentUser, clock);

                return VideoMapper.ToDto(v,
                    teamMap.GetValueOrDefault(v.TeamId)?.Name ?? "Equipo",
                    coachMap.GetValueOrDefault(v.UploadedByCoachId)?.DisplayName ?? "Cuerpo técnico",
                    level, canDelete);
            })
            .ToList();
    }
}

/// <summary>Abre el vídeo para servirlo en streaming a partir de un ticket de reproducción.</summary>
public sealed record OpenVideoStreamQuery(string Ticket) : IQuery<VideoStreamDto>;

internal sealed class OpenVideoStreamHandler(
    IVideoTickets tickets,
    IMatchVideoStore videos,
    IVideoStorage storage) : IQueryHandler<OpenVideoStreamQuery, VideoStreamDto>
{
    public async Task<VideoStreamDto> Handle(OpenVideoStreamQuery query, CancellationToken cancellationToken)
    {
        var videoId = tickets.Resolve(query.Ticket)
            ?? throw new ForbiddenException(ErrorCode.VideoTicketInvalid);

        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, videoId);

        var content = await storage.OpenReadAsync(video.StorageKey, cancellationToken);
        return new VideoStreamDto(content, video.ContentType, video.FileName, video.SizeBytes);
    }
}
