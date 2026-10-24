using TercerTiempo.Api.Infrastructure;
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
            .WithSummary("Añadir un filial a la licencia del club")
            .WithDescription("El primer equipo y los filiales del club quedan vinculados al darse de alta; esto es para un filial que aparece después (availableFilials de GET /api/clubs/me). Solo equipos del mismo club del catálogo: la licencia del club no cubre equipos de otros clubes. Un equipo solo puede pertenecer a un club.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/teams/{teamId:guid}", (Guid teamId, IClubsRepository clubs, CancellationToken ct) => clubs.RemoveTeamAsync(teamId, ct))
            .WithName("RemoveClubTeam")
            .WithSummary("Quitar un filial de la licencia del club")
            .WithDescription("Deja de estar cubierto por la licencia del club. Los nombramientos ya emitidos para ese equipo no cambian. El club conserva al menos un equipo.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/teams/{teamId:guid}/crest", async (Guid teamId, IFormFile file, IClubsRepository clubs, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await clubs.UploadTeamCrestAsync(teamId, new CrestUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .RequireLicense(LicenseScope.Team)
            .WithName("UploadClubTeamCrest")
            .WithSummary("Cambiar el escudo de un equipo del club")
            .WithDescription("Solo cuentas de club y solo para sus equipos vinculados. multipart/form-data con el campo file (PNG, JPG, WebP, SVG o GIF hasta 2 MB). Sustituye el escudo guardado; crestUrl cambia de versión para que los clientes recarguen la imagen.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/teams/{teamId:guid}/roster", (Guid teamId, RosterEntryRequest request, IClubsRepository clubs, CancellationToken ct) => clubs.AddRosterEntryAsync(teamId, request, ct))
            .RequireLicense(LicenseScope.Team)
            .WithName("AddClubRosterEntry")
            .WithSummary("Añadir una ficha a la plantilla de un equipo del club")
            .WithDescription("Solo cuentas de club y solo para sus equipos. La ficha se añade a la plantilla de la temporada en curso (la que se importa al crear la plantilla interna) y no la toca la sincronización con el proveedor. Devuelve la plantilla completa.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/teams/{teamId:guid}/roster/{entryId:guid}", (Guid teamId, Guid entryId, RosterEntryRequest request, IClubsRepository clubs, CancellationToken ct) => clubs.UpdateRosterEntryAsync(teamId, entryId, request, ct))
            .RequireLicense(LicenseScope.Team)
            .WithName("UpdateClubRosterEntry")
            .WithSummary("Corregir una ficha de la plantilla")
            .WithDescription("Nombre, posición y dorsal. A partir de la corrección manda el club: el proveedor ya no sobrescribe esa ficha.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/teams/{teamId:guid}/roster/{entryId:guid}", (Guid teamId, Guid entryId, IClubsRepository clubs, CancellationToken ct) => clubs.RemoveRosterEntryAsync(teamId, entryId, ct))
            .RequireLicense(LicenseScope.Team)
            .WithName("RemoveClubRosterEntry")
            .WithSummary("Retirar una ficha de la plantilla")
            .WithDescription("Las fichas del proveedor se ocultan (así no vuelven al sincronizar); las creadas por el club se borran.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/teams/{teamId:guid}/roster/{entryId:guid}", (Guid teamId, Guid entryId, IClubsRepository clubs, CancellationToken ct) => clubs.GetRosterEntryAsync(teamId, entryId, ct))
            .RequireLicense(LicenseScope.Team)
            .WithName("GetClubRosterEntry")
            .WithSummary("Ficha de un jugador de la plantilla con sus actas")
            .WithDescription("Solo cuentas de club y solo para sus equipos. Devuelve la ficha con sus totales oficiales, partido a partido según las actas de la federación y, si el cuerpo técnico lleva la plantilla en Tercer Tiempo, también sus actas internas.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/teams/{teamId:guid}/roster/{entryId:guid}/photo", async (Guid teamId, Guid entryId, IFormFile file, IClubsRepository clubs, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await clubs.UploadRosterPhotoAsync(teamId, entryId, new CrestUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .RequireLicense(LicenseScope.Team)
            .WithName("UploadClubRosterPhoto")
            .WithSummary("Cambiar la foto de un jugador de la plantilla")
            .WithDescription("Solo cuentas de club y solo para sus equipos. multipart/form-data con el campo file (PNG, JPG o WebP hasta 2 MB). Sustituye la foto guardada y devuelve la plantilla completa.")
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
