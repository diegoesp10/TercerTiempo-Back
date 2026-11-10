using System.ComponentModel;
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
    bool CanDelete,
    [property: Description("Uploaded (en cola para comprimir), Processing, Ready o Failed. Se ve y se descarga en todos: hasta Ready se sirve el original.")]
    VideoStatus Status,
    [property: Description("Avance de la compresión en %, mientras está en Processing.")]
    int Progress,
    [property: Description("Lo que ocupaba el archivo subido, cuando se ha sustituido por el comprimido.")]
    long? OriginalSizeBytes,
    int? DurationSeconds,
    [property: Description("Alto del vídeo en píxeles (1080 = Full HD) cuando se conoce.")]
    int? Height,
    [property: Description("Por qué no se pudo comprimir (solo para el club y el cuerpo técnico).")]
    string? ProcessingError);

public sealed record VideoTicketDto(Guid VideoId, string Ticket, DateTimeOffset ExpiresAt);

public sealed record VideoStreamDto(Stream Content, string ContentType, string FileName, long Length);

public sealed record VideoUpload(string FileName, string ContentType, long Length, Stream Content);

internal static class VideoMapper
{
    public static MatchVideoDto ToDto(MatchVideo video, string teamName, string uploadedBy, VideoAccessLevel access, bool canDownload, bool canDelete) =>
        new(video.Id, video.MatchId, video.SquadId, video.TeamId, teamName, video.Part, video.Title, video.FileName, video.ContentType,
            video.SizeBytes, video.UploadedAt, uploadedBy, access, VideoAccess.CanWatch(access), canDownload, canDelete,
            video.Status, video.ProcessingProgress, video.OriginalSizeBytes, video.DurationSeconds, video.Height,
            canDelete ? video.ProcessingError : null);
}
