using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

public enum VideoAccessLevel
{
    None,

    Locked,

    Owner,
    Purchased,
    Free,
}

public sealed record MatchVideoDto(
    Guid Id,
    Guid MatchId,
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    int PriceCents,
    string Currency,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    VideoAccessLevel Access,
    bool CanWatch,
    bool CanDelete);

public sealed record VideoTicketDto(Guid VideoId, string Ticket, DateTimeOffset ExpiresAt);

public sealed record VideoStreamDto(Stream Content, string ContentType, string FileName, long Length);

public sealed record VideoUpload(string FileName, string ContentType, long Length, Stream Content);

internal static class VideoMapper
{
    public static MatchVideoDto ToDto(MatchVideo video, string teamName, string uploadedBy, VideoAccessLevel access, bool canDelete) =>
        new(video.Id, video.MatchId, video.SquadId, video.TeamId, teamName, video.Title, video.FileName, video.ContentType,
            video.SizeBytes, video.PriceCents, video.Currency, video.UploadedAt, uploadedBy, access,
            access is VideoAccessLevel.Owner or VideoAccessLevel.Purchased or VideoAccessLevel.Free, canDelete);
}
