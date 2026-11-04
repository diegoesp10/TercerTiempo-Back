using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

public enum VideoAccessLevel
{
    None,

    // Plantilla pública: se puede solicitar el acceso al club.
    Locked,

    Owner,

    // Con un pase del partido que incluye el vídeo.
    Pass,

    // El administrador: lo ve para el mantenimiento de la web, sin pase ni gestionarlo.
    Admin,
}

public sealed record MatchVideoDto(
    Guid Id,
    Guid MatchId,
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    VideoPart Part,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    VideoAccessLevel Access,
    bool CanWatch,
    bool CanDownload,
    bool CanDelete);

public sealed record VideoTicketDto(Guid VideoId, string Ticket, DateTimeOffset ExpiresAt);

public sealed record VideoStreamDto(Stream Content, string ContentType, string FileName, long Length);

public sealed record VideoUpload(string FileName, string ContentType, long Length, Stream Content);

internal static class VideoMapper
{
    public static MatchVideoDto ToDto(MatchVideo video, string teamName, string uploadedBy, VideoAccessLevel access, bool canDownload, bool canDelete) =>
        new(video.Id, video.MatchId, video.SquadId, video.TeamId, teamName, video.Part, video.Title, video.FileName, video.ContentType,
            video.SizeBytes, video.UploadedAt, uploadedBy, access, VideoAccess.CanWatch(access), canDownload, canDelete);
}
