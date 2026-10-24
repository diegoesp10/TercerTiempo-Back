using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Videos;

public sealed record ListMatchVideosQuery(Guid MatchId) : IQuery<IReadOnlyList<MatchVideoDto>>;

internal sealed class ListMatchVideosHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ISquadStore squads,
    IClubStore clubs,
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
        var coachMap = (await coaches.GetByIdsAsync(list.Where(v => v.UploadedByCoachId != null).Select(v => v.UploadedByCoachId!.Value), cancellationToken)).ToDictionary(c => c.Id);
        var clubMap = new Dictionary<Guid, Club>();
        foreach (var uploaderId in list.Where(v => v.UploadedByClubId != null).Select(v => v.UploadedByClubId!.Value).Distinct())
            if (await clubs.GetByIdAsync(uploaderId, cancellationToken) is { } uploader) clubMap[uploaderId] = uploader;

        var purchasedIds = currentUser.CoachId is { } coachId
            ? (await purchases.GetByCoachAsync(coachId, cancellationToken)).Select(p => p.VideoId).ToHashSet()
            : [];

        // Comprar cualquier parte de un partido da acceso a las demás partes de esa plantilla.
        var purchasedSquads = list.Where(v => purchasedIds.Contains(v.Id)).Select(v => v.SquadId).ToHashSet();
        var club = currentUser.ClubId is { } clubId ? await clubs.GetByIdAsync(clubId, cancellationToken) : null;

        return list
            .OrderBy(v => v.Part)
            .ThenBy(v => v.UploadedAt)
            .Select(v =>
            {
                var ownerSquad = squadMap.GetValueOrDefault(v.SquadId);
                var level = VideoAccess.LevelFor(v, ownerSquad, currentUser, purchasedSquads.Contains(v.SquadId), clock, club?.Manages(v.TeamId) == true);
                var canDelete = ownerSquad is not null && (SquadAccess.IsStaff(ownerSquad, currentUser, clock) || club?.Manages(v.TeamId) == true);

                return VideoMapper.ToDto(v,
                    teamMap.GetValueOrDefault(v.TeamId)?.Name ?? "Equipo",
                    v.UploadedByClubId is { } id ? clubMap.GetValueOrDefault(id)?.Name ?? "Club" : v.UploadedByCoachId is { } coach ? coachMap.GetValueOrDefault(coach)?.DisplayName ?? "Cuerpo técnico" : "Equipo",
                    level, canDelete);
            })
            .ToList();
    }
}

public sealed record OpenVideoStreamQuery(Guid VideoId, string Ticket) : IQuery<VideoStreamDto>;

internal sealed class OpenVideoStreamHandler(
    IVideoTickets tickets,
    IMatchVideoStore videos,
    IVideoStorage storage) : IQueryHandler<OpenVideoStreamQuery, VideoStreamDto>
{
    public async Task<VideoStreamDto> Handle(OpenVideoStreamQuery query, CancellationToken cancellationToken)
    {
        var grant = tickets.Resolve(query.Ticket)
            ?? throw new ForbiddenException(ErrorCode.VideoTicketInvalid);
        if (grant.VideoId != query.VideoId) throw new ForbiddenException(ErrorCode.VideoTicketInvalid);

        var video = await videos.GetByIdAsync(grant.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, grant.VideoId);
        if (grant.RequiresPublicVideo && !video.IsPublic) throw new ForbiddenException(ErrorCode.VideoNotAllowed);

        var content = await storage.OpenReadAsync(video.StorageKey, cancellationToken);
        return new VideoStreamDto(content, video.ContentType, video.FileName, video.SizeBytes);
    }
}
