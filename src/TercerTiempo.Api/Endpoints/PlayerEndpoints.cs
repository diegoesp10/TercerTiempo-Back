using TercerTiempo.Application.Players.JoinTeam;
using TercerTiempo.Repository.Players;

namespace TercerTiempo.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/players").WithTags("Jugadores").RequireAuthorization();

        group.MapGet("/me", (IPlayersRepository players, CancellationToken ct) => players.GetCurrentAsync(ct))
            .WithSummary("Perfil del jugador autenticado y su equipo en la temporada actual");

        group.MapPost("/me/team", (JoinTeamCommand command, IPlayersRepository players, CancellationToken ct) => players.JoinTeamAsync(command, ct))
            .WithSummary("Unirse a la plantilla de un equipo (verificada si coincide con la plantilla scrapeada)")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/me/team", (IPlayersRepository players, CancellationToken ct) => players.LeaveTeamAsync(ct))
            .WithSummary("Abandonar la plantilla actual")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
