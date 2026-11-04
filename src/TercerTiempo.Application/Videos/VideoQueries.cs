using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Passes;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Passes;
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
    IMatchPassStore passes,
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

        var insight = await PassAccess.AllAsync(currentUser, passes, cancellationToken);
        var club = currentUser.ClubId is { } clubId ? await clubs.GetByIdAsync(clubId, cancellationToken) : null;

        return list
            .OrderBy(v => v.Part)
            .ThenBy(v => v.UploadedAt)
            .Select(v =>
            {
                var ownerSquad = squadMap.GetValueOrDefault(v.SquadId);
                var pass = insight.GetValueOrDefault((v.SquadId, v.MatchId), MatchInsight.None);
                var clubOwner = club?.Manages(v.TeamId) == true;
                var level = VideoAccess.LevelFor(v, ownerSquad, currentUser, pass, clock, clubOwner);
                var manager = ownerSquad is not null && (SquadAccess.IsStaff(ownerSquad, currentUser, clock) || clubOwner);

                return (Level: level, Dto: VideoMapper.ToDto(v,
                    teamMap.GetValueOrDefault(v.TeamId)?.Name ?? "Equipo",
                    v.UploadedByClubId is { } id ? clubMap.GetValueOrDefault(id)?.Name ?? "Club" : v.UploadedByCoachId is { } coach ? coachMap.GetValueOrDefault(coach)?.DisplayName ?? "Cuerpo técnico" : "Equipo",
                    level, VideoAccess.CanDownload(level, manager, pass), manager));
            })
            .Where(x => x.Level != VideoAccessLevel.None)
            .Select(x => x.Dto)
            .ToList();
    }
}

public sealed record OpenVideoStreamQuery(Guid VideoId, string Ticket, bool Download = false) : IQuery<VideoStreamDto>;

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

        // El ticket de reproducción no sirve para descargar: la descarga tiene el suyo.
        if (query.Download && !grant.Download) throw new ForbiddenException(ErrorCode.VideoDownloadDenied);

        var video = await videos.GetByIdAsync(grant.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
            throw new NotFoundException(ErrorCode.VideoNotFound, grant.VideoId);

        var content = await storage.OpenReadAsync(video.StorageKey, cancellationToken);
        return new VideoStreamDto(content, video.ContentType, video.FileName, video.SizeBytes);
    }
}
