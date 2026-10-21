using TercerTiempo.Repository.Videos;

namespace TercerTiempo.Api.Endpoints;

internal static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/videos").WithTags("Vídeos");

        group.MapPost("/{videoId:guid}/purchase", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.PurchaseAsync(videoId, ct))
            .RequireAuthorization()
            .WithName("PurchaseVideo")
            .WithSummary("Comprar el acceso a un vídeo")
            .WithDescription("Solo técnicos de otras plantillas. Cobra el precio del vídeo a través de la pasarela configurada (de momento simulada: Payments) y concede acceso permanente.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{videoId:guid}/ticket", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.IssueTicketAsync(videoId, ct))
            .RequireAuthorization()
            .WithName("IssueVideoTicket")
            .WithSummary("Ticket de reproducción")
            .WithDescription("Comprueba el acceso al vídeo y emite un ticket de corta vida para el reproductor, que no puede enviar el JWT: GET /api/videos/{videoId}/stream?ticket=...")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{videoId:guid}/stream", async (Guid videoId, string ticket, IVideosRepository videos, CancellationToken ct) =>
            {
                var stream = await videos.OpenStreamAsync(ticket, ct);
                // Rangos habilitados: el reproductor pide trozos y permite saltar en la línea de tiempo.
                return Results.File(stream.Content, stream.ContentType, enableRangeProcessing: true);
            })
            .WithName("StreamVideo")
            .WithSummary("Reproducir un vídeo")
            .WithDescription("Sirve el archivo con soporte de rangos (Range/206). Requiere un ticket vigente de POST /api/videos/{videoId}/ticket.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{videoId:guid}", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.DeleteAsync(videoId, ct))
            .RequireAuthorization()
            .WithName("DeleteVideo")
            .WithSummary("Eliminar un vídeo")
            .WithDescription("Solo el cuerpo técnico de la plantilla que lo subió.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
