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
            .WithDescription("Cuentas de jugador sin plantilla (por ejemplo tras desvincularse). Devuelve únicamente la ficha asociada al código personal; nunca la lista de fichas libres. Antes del alta se usa GET /api/auth/invite/{code}.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/squad", (JoinSquadAsPlayerCommand command, IPlayersRepository players, CancellationToken ct) => players.JoinSquadAsync(command, ct))
            .WithName("JoinSquadAsPlayer")
            .WithSummary("Vincularse a la ficha de una plantilla")
            .WithDescription("Cuentas de jugador ya existentes: con el código personal de un solo uso generado por su club o entrenador. La API determina la ficha a partir del código (no admite elegir jugador) y la cuenta pasa a llamarse como la ficha. Un jugador solo puede estar vinculado a una plantilla. Las cuentas nuevas se vinculan ya en POST /api/auth/register.")
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
