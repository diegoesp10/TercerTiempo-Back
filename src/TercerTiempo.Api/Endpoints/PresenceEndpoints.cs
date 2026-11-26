using System.ComponentModel;
using TercerTiempo.Repository.Alerts;
using TercerTiempo.Repository.Presence;

namespace TercerTiempo.Api.Endpoints;

internal static class PresenceEndpoints
{
    public sealed record PresenceRequest(string? Path);

    public sealed record PresencePulse(
        [property: Description("Huella de los avisos de la cuenta: si no coincide con el stamp de los que tiene la campanita, hay que volver a pedirlos.")]
        string AlertsStamp);

    public static IEndpointRouteBuilder MapPresenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Sin filtro de licencia: cualquier cuenta con la web abierta cuenta como gente en sesión.
        var group = app.MapGroup("/api/presence").WithTags("Sesión").RequireAuthorization();

        group.MapPost("/", async (PresenceRequest request, HttpContext http, IPresenceRepository presence, IAlertsRepository alerts, CancellationToken ct) =>
            {
                await presence.RecordAsync(request.Path, http.Request.Headers.UserAgent.ToString(), ct);
                return new PresencePulse(await alerts.GetStampAsync(ct));
            })
            .WithName("RecordPresence")
            .WithSummary("Latido de la sesión")
            .WithDescription("La web lo manda cada minuto mientras la pestaña está a la vista, al cambiar de página y al volver a la pestaña, con la página en la que está. Sirve para la lista de gente en sesión de la administración (no guarda nada en la base de datos) y devuelve la huella de los avisos de la cuenta (una consulta): la campanita solo vuelve a pedirlos cuando no coincide con la suya.");

        group.MapDelete("/", async (IPresenceRepository presence, CancellationToken ct) =>
            {
                await presence.EndAsync(ct);
                return Results.NoContent();
            })
            .WithName("EndPresence")
            .WithSummary("Fin de la sesión")
            .WithDescription("Al salir de la cuenta: la sesión deja de aparecer al momento en la lista de gente en sesión.")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
