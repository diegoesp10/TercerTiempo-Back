using TercerTiempo.Repository.Clubs;

namespace TercerTiempo.Api.Endpoints;

internal static class ClubEndpoints
{
    public static IEndpointRouteBuilder MapClubEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clubs/me").WithTags("Clubes").RequireAuthorization();

        group.MapGet("/", (IClubsRepository clubs, CancellationToken ct) => clubs.GetMineAsync(ct))
            .WithName("GetMyClub")
            .WithSummary("Mi club")
            .WithDescription("Solo cuentas de club. Equipos vinculados (con su competición, temporada y si ya tienen plantilla) y todos los nombramientos emitidos, con el código de acceso de los que aún no se han canjeado.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/teams", (ClubTeamRequest request, IClubsRepository clubs, CancellationToken ct) => clubs.AddTeamAsync(request.TeamId, ct))
            .WithName("AddClubTeam")
            .WithSummary("Vincular un equipo al club")
            .WithDescription("Uno de los equipos publicados por el proveedor (GET /api/teams). Un equipo solo puede pertenecer a un club.")
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
