using TercerTiempo.Repository.Stats;

namespace TercerTiempo.Api.Endpoints;

internal static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats/dashboard", (IStatsRepository stats, CancellationToken ct) => stats.GetDashboardAsync(ct))
            .RequireAuthorization()
            .WithTags("Estadísticas")
            .WithName("GetPlayerDashboard")
            .WithSummary("Panel del jugador")
            .WithDescription("Estadísticas individuales (totales y por 90'), impacto en el equipo (minutos, cuota de goles, resultados con y sin el jugador) y comparativa con su liga (equipo frente a la media y percentiles frente a otros jugadores registrados en la misma competición). Los percentiles aparecen cuando hay al menos dos jugadores con minutos en la plataforma.");

        app.MapGet("/api/league/table", (Guid? competitionId, IStatsRepository stats, CancellationToken ct) => stats.GetLeagueTableAsync(competitionId, ct))
            .WithTags("Liga")
            .WithName("GetLeagueTable")
            .WithSummary("Clasificación de una competición")
            .WithDescription("Tal y como la publica el proveedor de datos en la última sincronización. Sin competitionId se usa la del equipo del jugador autenticado. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
