using TercerTiempo.Repository.Presence;

namespace TercerTiempo.Api.Endpoints;

internal static class PresenceEndpoints
{
    public sealed record PresenceRequest(string? Path);

    public static IEndpointRouteBuilder MapPresenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Sin filtro de licencia: cualquier cuenta con la web abierta cuenta como gente en sesión.
        var group = app.MapGroup("/api/presence").WithTags("Sesión").RequireAuthorization();

        group.MapPost("/", async (PresenceRequest request, HttpContext http, IPresenceRepository presence, CancellationToken ct) =>
            {
                await presence.RecordAsync(request.Path, http.Request.Headers.UserAgent.ToString(), ct);
                return Results.NoContent();
            })
            .WithName("RecordPresence")
            .WithSummary("Latido de la sesión")
            .WithDescription("La web lo manda cada minuto mientras la pestaña está a la vista, con la página en la que está. Sirve para la lista de gente en sesión de la administración; no guarda nada en la base de datos.")
            .Produces(StatusCodes.Status204NoContent);

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
