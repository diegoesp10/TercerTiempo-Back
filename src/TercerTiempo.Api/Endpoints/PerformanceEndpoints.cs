using TercerTiempo.Repository.Performances;

namespace TercerTiempo.Api.Endpoints;

internal static class PerformanceEndpoints
{
    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/performances").WithTags("Rendimiento").RequireAuthorization();

        group.MapGet("/me", (IPerformancesRepository performances, CancellationToken ct) => performances.GetMineAsync(ct))
            .WithName("ListMyPerformances")
            .WithSummary("Mi historial de rendimiento")
            .WithDescription("Todos los partidos de la temporada actual en los que has registrado rendimiento, del más reciente al más antiguo, con los datos del partido.");

        return app;
    }
}
