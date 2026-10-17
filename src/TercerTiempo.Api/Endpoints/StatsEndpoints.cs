using TercerTiempo.Repository.Stats;

namespace TercerTiempo.Api.Endpoints;

internal static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats/dashboard", (IStatsRepository stats, CancellationToken ct) => stats.GetDashboardAsync(ct))
            .RequireAuthorization()
            .WithTags("Estadísticas")
            .WithSummary("Panel del jugador: individual, impacto en el equipo y comparativa con la liga");

        app.MapGet("/api/league/table", (IStatsRepository stats, CancellationToken ct) => stats.GetLeagueTableAsync(ct))
            .WithTags("Liga")
            .WithSummary("Clasificación de la temporada actual");

        return app;
    }
}
