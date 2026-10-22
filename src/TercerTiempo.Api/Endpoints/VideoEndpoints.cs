using TercerTiempo.Api.Infrastructure;
using TercerTiempo.Repository.Videos;

namespace TercerTiempo.Api.Endpoints;

internal static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder app)
    {
        // Los vídeos requieren licencia vigente del equipo que los subió (y del espectador si no es de ese equipo);
        // los enlaces compartidos solo la del equipo.
        var group = app.MapGroup("/api/videos").WithTags("Vídeos").RequireLicense(LicenseScope.Video);

        group.MapPost("/{videoId:guid}/purchase", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.PurchaseAsync(videoId, ct))
            .RequireAuthorization()
            .WithName("PurchaseVideo")
            .WithSummary("Comprar el acceso a un vídeo")
            .WithDescription("Solo técnicos de otras plantillas. Cobra el precio del vídeo a través de la pasarela configurada (de momento simulada: Payments) y concede acceso permanente.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{videoId:guid}/ticket", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.IssueTicketAsync(videoId, ct))
            .WithName("IssueVideoTicket")
            .WithSummary("Ticket de reproducción")
            .WithDescription("Comprueba el acceso al vídeo y emite un ticket de corta vida para el reproductor, que no puede enviar el JWT: GET /api/videos/{videoId}/stream?ticket=... Un vídeo público no necesita sesión.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{videoId:guid}/stream", async (Guid videoId, string ticket, HttpContext http, IVideosRepository videos, CancellationToken ct) =>
            {
                var stream = await videos.OpenStreamAsync(videoId, ticket, ct);
                http.Response.Headers.CacheControl = "no-store";
                // Rangos habilitados: el reproductor pide trozos y permite saltar en la línea de tiempo.
                return Results.File(stream.Content, stream.ContentType, enableRangeProcessing: true);
            })
            .WithName("StreamVideo")
            .WithSummary("Reproducir un vídeo")
            .WithDescription("Sirve el archivo con soporte de rangos (Range/206). Requiere un ticket vigente de POST /api/videos/{videoId}/ticket.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{videoId:guid}/visibility", (Guid videoId, VideoVisibilityRequest request, IVideosRepository videos, CancellationToken ct) => videos.SetVisibilityAsync(videoId, request.Visibility, ct))
            .RequireAuthorization()
            .WithName("SetVideoVisibility")
            .WithSummary("Hacer público o privado un vídeo")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente de la plantilla. Público: cualquiera que llegue al partido lo ve sin pagar. Privado: su plantilla, su club y quien lo haya comprado.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{videoId:guid}/shares", (Guid videoId, VideoShareRequest request, IVideosRepository videos, CancellationToken ct) => videos.CreateShareLinkAsync(videoId, request.ExpiresInHours, ct))
            .RequireAuthorization()
            .WithName("CreateVideoShareLink")
            .WithSummary("Generar un enlace público con caducidad para ver el vídeo")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente de la plantilla. El enlace del visor es {frontal}/ver/{token} y deja de valer al caducar o al revocarlo.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{videoId:guid}/shares", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.ListShareLinksAsync(videoId, ct))
            .RequireAuthorization()
            .WithName("ListVideoShareLinks")
            .WithSummary("Enlaces generados para un vídeo (vigentes y caducados)")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{videoId:guid}/shares/{linkId:guid}", (Guid videoId, Guid linkId, IVideosRepository videos, CancellationToken ct) => videos.RevokeShareLinkAsync(videoId, linkId, ct))
            .RequireAuthorization()
            .WithName("RevokeVideoShareLink")
            .WithSummary("Revocar un enlace")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/shared/{token}", (string token, IVideosRepository videos, CancellationToken ct) => videos.GetSharedAsync(token, ct))
            .RequireLicense(LicenseScope.SharedVideo)
            .WithName("GetSharedVideo")
            .WithSummary("Datos del vídeo de un enlace compartido")
            .WithDescription("Público, sin sesión. 403 si el enlace no existe, caducó o se revocó.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/shared/{token}/stream", async (string token, HttpContext http, IVideosRepository videos, CancellationToken ct) =>
            {
                var stream = await videos.OpenSharedStreamAsync(token, ct);
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(stream.Content, stream.ContentType, enableRangeProcessing: true);
            })
            .RequireLicense(LicenseScope.SharedVideo)
            .WithName("StreamSharedVideo")
            .WithSummary("Reproducir el vídeo de un enlace compartido")
            .WithDescription("Público, sin sesión, con soporte de rangos (Range/206).")
            .ProducesProblem(StatusCodes.Status403Forbidden);

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
