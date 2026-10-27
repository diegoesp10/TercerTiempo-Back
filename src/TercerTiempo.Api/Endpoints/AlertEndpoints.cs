using TercerTiempo.Repository.Alerts;

namespace TercerTiempo.Api.Endpoints;

internal static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        // Sin filtro de licencia: el club tiene que ver el aviso de que la suya acaba o ha acabado.
        var group = app.MapGroup("/api/clubs/me/alerts").WithTags("Avisos").RequireAuthorization();

        group.MapGet("/", (IAlertsRepository alerts, CancellationToken ct) => alerts.GetMineAsync(ct))
            .WithName("GetClubAlerts")
            .WithSummary("Avisos del club")
            .WithDescription("Solo cuentas de club (ni el cuerpo técnico ni los jugadores). Los últimos avisos de la campanita, del más reciente al más antiguo, y cuántos siguen vigentes sin leer. De momento, los de caducidad de la licencia: cuando acaba sin renovarse, con los días de antelación de la administración (15 y 5 por defecto).")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{alertId:guid}/read", (Guid alertId, IAlertsRepository alerts, CancellationToken ct) => alerts.MarkReadAsync(alertId, ct))
            .WithName("MarkClubAlertRead")
            .WithSummary("Marcar un aviso como leído")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read", (IAlertsRepository alerts, CancellationToken ct) => alerts.MarkAllReadAsync(ct))
            .WithName("MarkAllClubAlertsRead")
            .WithSummary("Marcar todos los avisos como leídos")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
