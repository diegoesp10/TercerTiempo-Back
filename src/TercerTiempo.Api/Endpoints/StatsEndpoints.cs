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

        app.MapGet("/api/league/predictions", (Guid competitionId, int? matchday, IStatsRepository stats, CancellationToken ct) =>
                stats.GetPredictionAsync(competitionId, matchday, ct))
            .AllowAnonymous()
            .WithTags("Liga")
            .WithName("GetLeaguePrediction")
            .WithSummary("Previsor de resultados de una liga")
            .WithDescription("Probabilidad de cada equipo de ser campeón, acabar en ascenso o promoción y descender, y de acabar en cada puesto, con lo que se sabía en una jornada (matchday; sin él, la última con resultados): una fuerza de ataque y defensa por equipo sacada de sus goles hasta esa jornada y 10.000 simulaciones del resto de la temporada (Poisson con ventaja de campo). Solo informativo; plazas habituales de la categoría. Público, sin token, en la temporada elegida (X-Season).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/league/predictions/evolution", (Guid competitionId, IStatsRepository stats, CancellationToken ct) =>
                stats.GetPredictionEvolutionAsync(competitionId, ct))
            .AllowAnonymous()
            .WithTags("Liga")
            .WithName("GetLeaguePredictionEvolution")
            .WithSummary("Evolución del previsor jornada a jornada")
            .WithDescription("Para cada jornada con resultados, la probabilidad de cada equipo de ser campeón, ascender o promocionar y descender, y su puesto en esa jornada (3.000 simulaciones por jornada). Público, sin token.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
