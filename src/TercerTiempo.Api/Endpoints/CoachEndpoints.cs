using TercerTiempo.Application.Coaches.Appointments;
using TercerTiempo.Repository.Coaches;

namespace TercerTiempo.Api.Endpoints;

internal static class CoachEndpoints
{
    public static IEndpointRouteBuilder MapCoachEndpoints(this IEndpointRouteBuilder app)
    {
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

        return app;
    }
}
