using TercerTiempo.Repository.Admin;

namespace TercerTiempo.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: restringir a un rol "Admin" cuando se añadan roles. De momento basta con estar autenticado.
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization();

        group.MapPost("/sync", (IAdminRepository admin, CancellationToken ct) => admin.SyncSeasonAsync(ct))
            .WithName("SyncSeason")
            .WithSummary("Forzar sincronización con el proveedor de datos")
            .WithDescription("Descarga todas las ligas del proveedor configurado (FootballData:Provider) con sus equipos, partidos, clasificación y plantillas y las vuelca en la base de datos. Idempotente; cada liga se guarda por separado y los errores se devuelven en errors. El worker lo ejecuta además al arrancar y cada FootballData:SyncIntervalHours horas. Con futbolasturiano.es tarda del orden de un minuto.");

        return app;
    }
}
