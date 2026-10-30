using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Repository.Coaches;

namespace TercerTiempo.Api.Endpoints;

internal static class CoachEndpoints
{
    public static IEndpointRouteBuilder MapCoachEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/coaches/{coachId:guid}/photo", async (Guid coachId, HttpContext context, ICoachesRepository coaches, CancellationToken ct) =>
            {
                var photo = await coaches.OpenPhotoAsync(coachId, ct);
                if (photo is null) return Results.NotFound();

                context.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Stream(photo.Content, photo.ContentType);
            })
            .WithTags("Cuerpo técnico")
            .WithName("GetCoachPhoto")
            .WithSummary("Foto de un técnico")
            .WithDescription("Foto que ha subido el propio técnico (photoUrl apunta aquí, con la versión en la URL). 404 si no tiene.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/coaches/{coachId:guid}", (Guid coachId, ICoachesRepository coaches, CancellationToken ct) => coaches.GetProfileAsync(coachId, ct))
            .RequireAuthorization()
            .WithTags("Cuerpo técnico")
            .WithName("GetCoachProfile")
            .WithSummary("Perfil de un técnico")
            .WithDescription("Para cualquier cuenta con sesión: nombre, foto y los equipos que dirige o ha dirigido por los nombramientos de sus clubes (vigentes primero; los revocados no salen). isMe dice si es tu propio perfil.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var group = app.MapGroup("/api/coaches/me").WithTags("Cuerpo técnico").RequireAuthorization();

        group.MapPost("/appointments/redeem", (RedeemAppointmentCommand command, ICoachesRepository coaches, CancellationToken ct) => coaches.RedeemAppointmentAsync(command, ct))
            .WithName("RedeemAppointment")
            .WithSummary("Canjear un código de acceso del club")
            .WithDescription("Para técnicos que ya tienen cuenta: entra en el cuerpo técnico de la plantilla del equipo y temporada del nombramiento (la plantilla se crea si no existe, importando las fichas publicadas por el proveedor). Devuelve el perfil con sus plantillas y nombramientos.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/photo", async (IFormFile file, ICoachesRepository coaches, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await coaches.UploadPhotoAsync(new CrestUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadMyCoachPhoto")
            .WithSummary("Subir mi foto de técnico")
            .WithDescription("Solo el propio técnico. multipart/form-data con el campo file (PNG con fondo transparente recomendado; también JPG o WebP), hasta 3 MB. Sustituye la anterior.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/photo", (ICoachesRepository coaches, CancellationToken ct) => coaches.DeletePhotoAsync(ct))
            .WithName("DeleteMyCoachPhoto")
            .WithSummary("Quitar mi foto de técnico")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
