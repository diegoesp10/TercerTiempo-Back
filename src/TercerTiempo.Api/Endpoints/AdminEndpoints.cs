using TercerTiempo.Repository.Admin;

namespace TercerTiempo.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // TODO: restringir a un rol "Admin" cuando se añadan roles. De momento basta con estar autenticado.
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization();

        group.MapPost("/sync", (IAdminRepository admin, CancellationToken ct) => admin.SyncSeasonAsync(ct))
            .WithSummary("Fuerza una sincronización con el proveedor de datos de fútbol");

        return app;
    }
}
