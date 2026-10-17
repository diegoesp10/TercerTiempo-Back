using TercerTiempo.Repository.Competitions;

namespace TercerTiempo.Api.Endpoints;

internal static class CompetitionEndpoints
{
    public static IEndpointRouteBuilder MapCompetitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/competitions", (ICompetitionsRepository competitions, CancellationToken ct) => competitions.ListAsync(ct))
            .WithTags("Competiciones")
            .WithName("ListCompetitions")
            .WithSummary("Ligas disponibles")
            .WithDescription("Todas las ligas sincronizadas (categoría + grupo), con nivel (Professional, National, Regional, Cup, Youth), escalón, grupo, temporada en curso y número de equipos. Ordenadas por nivel, escalón y grupo. Público.");

        return app;
    }
}
