using TercerTiempo.Repository.Matches;

namespace TercerTiempo.Api.Endpoints;

internal static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches").WithTags("Partidos");

        group.MapGet("/{matchId:guid}", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.GetAsync(matchId, ct))
            .WithName("GetMatch")
            .WithSummary("Detalle de un partido")
            .WithDescription("Incluye los rendimientos registrados por los jugadores de la plataforma y, si se envía token, el tuyo en myPerformance. Público.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{matchId:guid}/performance", (Guid matchId, LogPerformanceRequest request, IMatchesRepository matches, CancellationToken ct) => matches.LogPerformanceAsync(matchId, request, ct))
            .RequireAuthorization()
            .WithName("LogPerformance")
            .WithSummary("Registrar o actualizar tu rendimiento en un partido")
            .WithDescription("Idempotente: una única ficha por jugador y partido, se sobrescribe en cada llamada. Requiere pertenencia Active en un equipo del partido y que el partido esté Played. Reglas: minutos 0-120, tiros a puerta ≤ tiros, dos amarillas ⇒ roja, sin minutos no hay acciones de juego.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
