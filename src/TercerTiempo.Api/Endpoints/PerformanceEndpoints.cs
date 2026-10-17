using TercerTiempo.Repository.Performances;

namespace TercerTiempo.Api.Endpoints;

internal static class PerformanceEndpoints
{
    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/performances").WithTags("Rendimiento").RequireAuthorization();

        group.MapGet("/me", (IPerformancesRepository performances, CancellationToken ct) => performances.GetMineAsync(ct))
            .WithSummary("Historial de rendimiento del jugador autenticado en la temporada actual");

        return app;
    }
}
