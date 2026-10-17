using TercerTiempo.Application.Players.JoinSquad;
using TercerTiempo.Repository.Players;

namespace TercerTiempo.Api.Endpoints;

internal static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/players/me").WithTags("Jugadores").RequireAuthorization();

        group.MapGet("/invite/{inviteCode}", (string inviteCode, IPlayersRepository players, CancellationToken ct) => players.GetInviteAsync(inviteCode, ct))
            .WithName("GetSquadInvite")
            .WithSummary("Consultar un código de invitación")
            .WithDescription("Devuelve la plantilla que hay detrás del código y las fichas que aún no tienen jugador vinculado, para que el jugador elija la suya.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/squad", (JoinSquadAsPlayerCommand command, IPlayersRepository players, CancellationToken ct) => players.JoinSquadAsync(command, ct))
            .WithName("JoinSquadAsPlayer")
            .WithSummary("Vincularse a la ficha de una plantilla")
            .WithDescription("Con el código de invitación del cuerpo técnico. Si no se indica squadPlayerId se busca una ficha sin vincular con el mismo nombre. Un jugador solo puede estar vinculado a una plantilla.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/squad", (IPlayersRepository players, CancellationToken ct) => players.LeaveSquadAsync(ct))
            .WithName("LeaveSquadAsPlayer")
            .WithSummary("Desvincularse de la plantilla")
            .WithDescription("La ficha y sus actas siguen en la plantilla; solo se rompe el vínculo con la cuenta.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/dashboard", (IPlayersRepository players, CancellationToken ct) => players.GetDashboardAsync(ct))
            .WithName("GetPlayerDashboard")
            .WithSummary("Panel del jugador")
            .WithDescription("Estadísticas calculadas a partir de las actas que registra su cuerpo técnico: individuales (totales y por 90'), impacto en el equipo (minutos, cuota de goles, resultados con y sin él) y comparativa con la liga. Los percentiles solo comparan con actas visibles (plantillas públicas y la suya) y aparecen con al menos dos jugadores con minutos.");

        group.MapGet("/performances", (IPlayersRepository players, CancellationToken ct) => players.GetMyPerformancesAsync(ct))
            .WithName("ListMyPerformances")
            .WithSummary("Mis actas")
            .WithDescription("Partidos en los que el cuerpo técnico ha registrado la línea del jugador, del más reciente al más antiguo.");

        return app;
    }
}
