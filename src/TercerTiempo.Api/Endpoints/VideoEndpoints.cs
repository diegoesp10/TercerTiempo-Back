using TercerTiempo.Api.Infrastructure;
using TercerTiempo.Repository.Videos;

namespace TercerTiempo.Api.Endpoints;

internal static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder app)
    {
        // Los vídeos nunca son públicos: los ven los de la plantilla (club, cuerpo técnico, jugadores), el administrador y
        // quien tenga un pase del partido que los incluya. Todos requieren la licencia vigente del equipo que los subió.
        var group = app.MapGroup("/api/videos").WithTags("Vídeos").RequireLicense(LicenseScope.Video);

        group.MapPost("/{videoId:guid}/ticket", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.IssueTicketAsync(videoId, false, ct))
            .RequireAuthorization()
            .WithName("IssueVideoTicket")
            .WithSummary("Ticket de reproducción")
            .WithDescription("Comprueba el acceso al vídeo (de la plantilla o con un pase del partido) y emite un ticket para el reproductor, que no puede enviar el JWT: GET /api/videos/{videoId}/stream?ticket=... El ticket se renueva mientras se usa (Videos:TicketMinutes) hasta un máximo de Videos:TicketMaxHours.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{videoId:guid}/stream", async (Guid videoId, string ticket, HttpContext http, IVideosRepository videos, CancellationToken ct) =>
            {
                var stream = await videos.OpenStreamAsync(videoId, ticket, false, ct);
                http.Response.Headers.CacheControl = "no-store";
                // Rangos habilitados: el reproductor pide trozos y permite saltar en la línea de tiempo.
                return Results.File(stream.Content, stream.ContentType, enableRangeProcessing: true);
            })
            .WithName("StreamVideo")
            .WithSummary("Reproducir un vídeo")
            .WithDescription("Sirve el archivo con soporte de rangos (Range/206). Requiere un ticket vigente de POST /api/videos/{videoId}/ticket.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{videoId:guid}/download-ticket", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.IssueTicketAsync(videoId, true, ct))
            .RequireAuthorization()
            .WithName("IssueVideoDownloadTicket")
            .WithSummary("Ticket de descarga")
            .WithDescription("Solo el club propietario, el cuerpo técnico vigente de la plantilla y quien tenga un pase del partido con descarga. Devuelve un ticket para GET /api/videos/{videoId}/download?ticket=...")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{videoId:guid}/download", async (Guid videoId, string ticket, HttpContext http, IVideosRepository videos, CancellationToken ct) =>
            {
                var stream = await videos.OpenStreamAsync(videoId, ticket, true, ct);
                http.Response.Headers.CacheControl = "no-store";
                // Con rangos, el navegador puede reanudar una descarga de varios GB que se corte.
                return Results.File(stream.Content, stream.ContentType, stream.FileName, enableRangeProcessing: true);
            })
            .WithName("DownloadVideo")
            .WithSummary("Descargar el archivo de un vídeo")
            .WithDescription("Sirve el archivo como adjunto, con soporte de rangos para reanudar. Requiere un ticket de descarga vigente; el de reproducción no sirve.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{videoId:guid}", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.DeleteAsync(videoId, ct))
            .RequireAuthorization()
            .WithName("DeleteVideo")
            .WithSummary("Eliminar un vídeo")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente de la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}