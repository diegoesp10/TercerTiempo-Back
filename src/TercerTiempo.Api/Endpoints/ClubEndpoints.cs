using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Repository.Clubs;

namespace TercerTiempo.Api.Endpoints;

internal static class ClubEndpoints
{
    public static IEndpointRouteBuilder MapClubEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/clubs/catalog", (string? search, IClubsRepository clubs, CancellationToken ct) => clubs.GetCatalogAsync(search, ct))
            .WithTags("Clubes")
            .WithName("GetClubCatalog")
            .WithSummary("Catálogo de clubes")
            .WithDescription("Clubes deducidos de los equipos que publica el proveedor (el primer equipo y sus filiales agrupados), con sus equipos, la competición en curso de cada uno y si el club ya tiene cuenta. Es la lista de la que se elige el nombre al darse de alta como club. Público.");

        var group = app.MapGroup("/api/clubs/me").WithTags("Clubes").RequireAuthorization();

        group.MapGet("/", (IClubsRepository clubs, CancellationToken ct) => clubs.GetMineAsync(ct))
            .WithName("GetMyClub")
            .WithSummary("Mi club")
            .WithDescription("Solo cuentas de club. Equipos vinculados (con su competición, temporada y si ya tienen plantilla) y todos los nombramientos emitidos, con el código de acceso de los que aún no se han canjeado.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/teams", (ClubTeamRequest request, IClubsRepository clubs, CancellationToken ct) => clubs.AddTeamAsync(request.TeamId, ct))
            .WithName("AddClubTeam")
            .WithSummary("Vincular otro equipo al club")
            .WithDescription("Los equipos del club quedan vinculados al darse de alta; esto es para los que no se reconocieron por el nombre. Uno de los equipos publicados por el proveedor (GET /api/teams), cuyo nombre se corresponda con el del club. Un equipo solo puede pertenecer a un club.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/teams/{teamId:guid}", (Guid teamId, IClubsRepository clubs, CancellationToken ct) => clubs.RemoveTeamAsync(teamId, ct))
            .WithName("RemoveClubTeam")
            .WithSummary("Desvincular un equipo del club")
            .WithDescription("Los nombramientos ya emitidos para ese equipo no cambian.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/teams/{teamId:guid}/crest", async (Guid teamId, IFormFile file, IClubsRepository clubs, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await clubs.UploadTeamCrestAsync(teamId, new CrestUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadClubTeamCrest")
            .WithSummary("Cambiar el escudo de un equipo del club")
            .WithDescription("Solo cuentas de club y solo para sus equipos vinculados. multipart/form-data con el campo file (PNG, JPG, WebP, SVG o GIF hasta 2 MB). Sustituye el escudo guardado; crestUrl cambia de versión para que los clientes recarguen la imagen.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/appointments", (CreateAppointmentRequest request, IClubsRepository clubs, CancellationToken ct) => clubs.CreateAppointmentAsync(request, ct))
            .WithName("CreateAppointment")
            .WithSummary("Nombrar a un técnico")
            .WithDescription("Crea el nombramiento de un técnico para un equipo del club en la temporada en curso y devuelve el código de acceso que hay que entregarle. Vale desde hoy hasta validUntil (por defecto, el final de la temporada; nunca más allá). Al canjearlo, el técnico entra en el cuerpo técnico de la plantilla del equipo (que se crea si no existe) hasta esa fecha.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/appointments/{appointmentId:guid}", (Guid appointmentId, UpdateAppointmentRequest request, IClubsRepository clubs, CancellationToken ct) => clubs.UpdateAppointmentAsync(appointmentId, request, ct))
            .WithName("UpdateAppointment")
            .WithSummary("Acortar o prorrogar un nombramiento")
            .WithDescription("Cambia el último día de validez (sin pasar del final de la temporada). Si el técnico ya está en la plantilla, su pertenencia se ajusta a la nueva fecha.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/appointments/{appointmentId:guid}", (Guid appointmentId, IClubsRepository clubs, CancellationToken ct) => clubs.RevokeAppointmentAsync(appointmentId, ct))
            .WithName("RevokeAppointment")
            .WithSummary("Revocar un nombramiento")
            .WithDescription("El código deja de valer y, si ya se había canjeado, el técnico sale del cuerpo técnico de la plantilla (sus actas registradas se conservan).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
