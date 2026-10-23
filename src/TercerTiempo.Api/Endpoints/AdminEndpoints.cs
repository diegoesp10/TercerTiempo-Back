using TercerTiempo.Repository.Admin;

namespace TercerTiempo.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: restringir a un rol "Admin" cuando se añadan roles. De momento basta con estar autenticado.
        app.MapGet("/api/sync/status", (IAdminRepository admin, CancellationToken ct) => admin.GetSyncStatusAsync(ct))
            .WithTags("Admin")
            .WithName("GetSyncStatus")
            .WithSummary("Estado de la sincronización")
            .WithDescription("Si está corriendo (fase «datos», liga en curso y progreso), cuándo terminó la última, su resumen y errores, y cuándo toca la siguiente (FootballData:SyncIntervalHours). Público: el frontal lo sondea para mostrar «actualizando datos».");

        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization();

        group.MapPost("/sync", (IAdminRepository admin, CancellationToken ct) => admin.SyncSeasonAsync(ct))
            .WithName("SyncSeason")
            .WithSummary("Forzar sincronización con el proveedor de datos")
            .WithDescription("Descarga todas las ligas del proveedor configurado (FootballData:Provider) con sus equipos, partidos, clasificación y plantillas y las vuelca en la base de datos. Idempotente; cada liga se guarda por separado y los errores se devuelven en errors. El worker lo ejecuta además al arrancar y cada FootballData:SyncIntervalHours horas. Con futbolasturiano.es tarda del orden de un minuto.");

        group.MapPost("/teams/{teamId:guid}/crest", (Guid teamId, ImportCrestRequest request, IAdminRepository admin, CancellationToken ct) => admin.ImportTeamCrestAsync(teamId, request.Url, ct))
            .WithName("ImportTeamCrest")
            .WithSummary("Importar el escudo de un equipo desde una URL")
            .WithDescription("Para los equipos que no tienen escudo guardado ni en el proveedor de datos: descarga la imagen (nunca de asturfutbol.es, de donde solo se leen actas) (png, jpg, gif, webp o svg, hasta 2 MB) y la guarda en Crests:Directory; a partir de entonces crestUrl apunta a GET /api/teams/{teamId}/crest.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
