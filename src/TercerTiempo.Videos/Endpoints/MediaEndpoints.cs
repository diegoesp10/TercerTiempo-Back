using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Hosting;
using TercerTiempo.Repository.Videos;

namespace TercerTiempo.Videos.Endpoints;

internal static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        MapUploads(app);
        MapVideos(app);
        return app;
    }

    private static void MapUploads(IEndpointRouteBuilder app)
    {
        // Toda subida es sobre una plantilla concreta: pasa por la puerta de licencias del equipo.
        var group = app.MapGroup("/media/squads").WithTags("Subidas").RequireAuthorization().RequireLicense(LicenseScope.Squad);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video", async (Guid squadId, Guid matchId, IFormFile file, string? title, VideoPart? part, HttpContext http, IVideosRepository videos, IOptions<VideoOptions> options, CancellationToken ct) =>
            {
                // El límite por defecto de Kestrel (30 MB) se queda corto para un partido entero.
                if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.Value.MaxSizeBytes + 1024 * 1024;

                await using var content = file.OpenReadStream();
                return await videos.UploadAsync(squadId, matchId, part ?? VideoPart.Full, title, new VideoUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadMatchVideo")
            .WithSummary("Subir el vídeo de un partido de una sola vez")
            .WithDescription("Club propietario o cuerpo técnico vigente. multipart/form-data con el campo file (mp4 o webm hasta Videos:MaxSizeMb), part (Full, FirstHalf o SecondHalf; Full por defecto) y title opcional. Para un partido entero usa la subida por trozos (…/video/uploads). El vídeo queda en cola para comprimirse; mientras tanto se sirve tal cual.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video/uploads", (Guid squadId, Guid matchId, BeginVideoUploadRequest request, IVideosRepository videos, CancellationToken ct) => videos.BeginUploadAsync(squadId, matchId, request, ct))
            .WithName("BeginVideoUpload")
            .WithSummary("Empezar una subida de vídeo por trozos")
            .WithDescription("Club propietario o cuerpo técnico vigente. Declara parte, archivo, tipo (mp4 o webm) y tamaño (hasta Videos:MaxSizeMb; un partido entero en 1080p ronda los 6 GB). 409 si no queda espacio en el servidor. Devuelve el id de la subida y el tamaño de trozo (Videos:ChunkSizeMb); los trozos se envían en orden a PUT …/video-uploads/{uploadId}/chunks/{index} y al final se llama a …/complete. La sesión caduca si pasan Videos:UploadExpiryHours horas sin recibir ningún trozo.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/video-uploads/{uploadId:guid}", (Guid squadId, Guid uploadId, IVideosRepository videos, CancellationToken ct) => videos.GetUploadAsync(squadId, uploadId, ct))
            .WithName("GetVideoUpload")
            .WithSummary("Estado de una subida por trozos (para reanudarla)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{squadId:guid}/video-uploads/{uploadId:guid}/chunks/{index:int}", async (Guid squadId, Guid uploadId, int index, HttpContext http, IVideosRepository videos, IOptions<VideoOptions> options, CancellationToken ct) =>
            {
                if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.Value.ChunkSizeBytes + 1024 * 1024;

                var length = http.Request.ContentLength ?? 0;
                return await videos.AppendChunkAsync(squadId, uploadId, index, length, http.Request.Body, ct);
            })
            .Accepts<byte[]>("application/octet-stream")
            .WithName("AppendVideoChunk")
            .WithSummary("Enviar un trozo del vídeo")
            .WithDescription("Cuerpo binario (application/octet-stream) con Content-Length. Los trozos van en orden y todos miden Videos:ChunkSizeMb salvo el último. Devuelve los bytes recibidos y el siguiente índice esperado.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/video-uploads/{uploadId:guid}/complete", (Guid squadId, Guid uploadId, IVideosRepository videos, CancellationToken ct) => videos.CompleteUploadAsync(squadId, uploadId, ct))
            .WithName("CompleteVideoUpload")
            .WithSummary("Cerrar la subida y publicar el vídeo")
            .WithDescription("Publica el vídeo tal cual se subió y lo pone en cola para comprimirlo (status Uploaded → Processing → Ready); se puede ver y descargar desde este momento.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{squadId:guid}/video-uploads/{uploadId:guid}", (Guid squadId, Guid uploadId, IVideosRepository videos, CancellationToken ct) => videos.CancelUploadAsync(squadId, uploadId, ct))
            .WithName("CancelVideoUpload")
            .WithSummary("Cancelar una subida y borrar el temporal")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapVideos(IEndpointRouteBuilder app)
    {
        // Los vídeos nunca son públicos: los ven los de la plantilla (club, cuerpo técnico, jugadores), el administrador y
        // quien tenga un pase del partido que los incluya. Todos requieren la licencia vigente del equipo que los subió.
        var group = app.MapGroup("/media/videos").WithTags("Vídeos").RequireLicense(LicenseScope.Video);

        group.MapPost("/{videoId:guid}/ticket", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.IssueTicketAsync(videoId, false, ct))
            .RequireAuthorization()
            .WithName("IssueVideoTicket")
            .WithSummary("Ticket de reproducción")
            .WithDescription("Comprueba el acceso al vídeo (de la plantilla o con un pase del partido) y emite un ticket para el reproductor, que no puede enviar el JWT: GET /media/videos/{videoId}/stream?ticket=... El ticket se renueva mientras se usa (Videos:TicketMinutes) hasta un máximo de Videos:TicketMaxHours.")
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
            .WithDescription("Sirve el archivo (el comprimido si ya está listo; si no, el original) con soporte de rangos (Range/206). Requiere un ticket vigente de POST /media/videos/{videoId}/ticket.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{videoId:guid}/download-ticket", (Guid videoId, IVideosRepository videos, CancellationToken ct) => videos.IssueTicketAsync(videoId, true, ct))
            .RequireAuthorization()
            .WithName("IssueVideoDownloadTicket")
            .WithSummary("Ticket de descarga")
            .WithDescription("Solo el club propietario, el cuerpo técnico vigente de la plantilla y quien tenga un pase del partido con descarga. Devuelve un ticket para GET /media/videos/{videoId}/download?ticket=...")
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
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente de la plantilla. Si se estaba comprimiendo, la compresión se para.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
