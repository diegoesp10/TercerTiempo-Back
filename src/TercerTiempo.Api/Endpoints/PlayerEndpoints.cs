using TercerTiempo.Application.Players.JoinTeam;
using TercerTiempo.Repository.Players;

namespace TercerTiempo.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/players").WithTags("Jugadores").RequireAuthorization();

        group.MapGet("/me", (IPlayersRepository players, CancellationToken ct) => players.GetCurrentAsync(ct))
            .WithName("GetCurrentPlayer")
            .WithSummary("Perfil del jugador autenticado")
            .WithDescription("Incluye su pertenencia (equipo, dorsal y estado Pending/Active) en la temporada actual, o null si aún no se ha unido a ningún equipo.");

        group.MapPost("/me/team", (JoinTeamCommand command, IPlayersRepository players, CancellationToken ct) => players.JoinTeamAsync(command, ct))
            .WithName("JoinTeam")
            .WithSummary("Unirse a la plantilla de un equipo")
            .WithDescription("Si se indica rosterEntryId (o el nombre coincide con una ficha de la plantilla scrapeada) la pertenencia nace verificada (Active) y ya permite registrar rendimiento; si no, queda Pending. Solo se puede pertenecer a una plantilla por temporada.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/me/team", (IPlayersRepository players, CancellationToken ct) => players.LeaveTeamAsync(ct))
            .WithName("LeaveTeam")
            .WithSummary("Abandonar la plantilla actual")
            .WithDescription("Los rendimientos ya registrados se conservan.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
