using TercerTiempo.Repository.Alerts;

namespace TercerTiempo.Api.Endpoints;

internal static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        // Sin filtro de licencia: el club tiene que ver el aviso de que la suya acaba o ha acabado, y el jugador o el
        // técnico el de una convocatoria aunque la licencia de su equipo esté caducada.
        Map(app.MapGroup("/api/alerts").WithTags("Avisos").RequireAuthorization(), "");
        // Ruta anterior, solo de los clubes: sigue valiendo.
        Map(app.MapGroup("/api/clubs/me/alerts").WithTags("Avisos").RequireAuthorization().ExcludeFromDescription(), "Club");

        return app;
    }

    private static void Map(RouteGroupBuilder group, string suffix)
    {
        group.MapGet("/", (IAlertsRepository alerts, CancellationToken ct) => alerts.GetMineAsync(ct))
            .WithName($"GetMyAlerts{suffix}")
            .WithSummary("Avisos de la cuenta")
            .WithDescription("La campanita de cualquier cuenta: los últimos avisos del más reciente al más antiguo y cuántos siguen vigentes sin leer. Club: caducidad de la licencia y solicitudes de pases. Jugador: convocatorias (vas o no vas, para contestar). Cuerpo técnico y club: convocados que no pueden ir y jugadores que avisan de que faltan a un entrenamiento.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{alertId:guid}/read", (Guid alertId, IAlertsRepository alerts, CancellationToken ct) => alerts.MarkReadAsync(alertId, ct))
            .WithName($"MarkAlertRead{suffix}")
            .WithSummary("Marcar un aviso como leído")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read", (IAlertsRepository alerts, CancellationToken ct) => alerts.MarkAllReadAsync(ct))
            .WithName($"MarkAllAlertsRead{suffix}")
            .WithSummary("Marcar todos los avisos como leídos")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
