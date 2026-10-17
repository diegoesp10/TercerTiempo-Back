using TercerTiempo.Repository.Matches;

namespace TercerTiempo.Api.Endpoints;

internal static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/matches/{matchId:guid}", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.GetAsync(matchId, ct))
            .WithTags("Partidos")
            .WithName("GetMatch")
            .WithSummary("Detalle de un partido")
            .WithDescription("Datos del partido y las actas de las plantillas de ambos equipos. Cada acta solo se incluye si el usuario puede verla (plantilla pública o propia); si no, llega vacía con visible = false. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
