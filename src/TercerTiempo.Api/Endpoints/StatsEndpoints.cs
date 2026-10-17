using TercerTiempo.Repository.Stats;

namespace TercerTiempo.Api.Endpoints;

internal static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/league/table", (Guid? competitionId, IStatsRepository stats, CancellationToken ct) => stats.GetLeagueTableAsync(competitionId, ct))
            .WithTags("Liga")
            .WithName("GetLeagueTable")
            .WithSummary("Clasificación de una competición")
            .WithDescription("Tal y como la publica el proveedor de datos en la última sincronización. Sin competitionId se usa la de la plantilla del usuario autenticado. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
