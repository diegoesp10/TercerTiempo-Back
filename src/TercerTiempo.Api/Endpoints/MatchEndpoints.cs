using TercerTiempo.Repository.Matches;

namespace TercerTiempo.Api.Endpoints;

internal static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/matches").WithTags("Partidos");

        group.MapGet("/{matchId:guid}", (Guid matchId, IMatchesRepository matches, CancellationToken ct) => matches.GetAsync(matchId, ct))
            .WithSummary("Detalle del partido con los rendimientos registrados")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{matchId:guid}/performance", (Guid matchId, LogPerformanceRequest request, IMatchesRepository matches, CancellationToken ct) => matches.LogPerformanceAsync(matchId, request, ct))
            .RequireAuthorization()
            .WithSummary("Registra o actualiza el rendimiento del jugador autenticado en el partido")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
