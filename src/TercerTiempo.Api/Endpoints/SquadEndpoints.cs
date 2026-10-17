using TercerTiempo.Application.Squads.ClaimTeam;
using TercerTiempo.Repository.Squads;

namespace TercerTiempo.Api.Endpoints;

internal static class SquadEndpoints
{
    public static IEndpointRouteBuilder MapSquadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/squads").WithTags("Plantillas").RequireAuthorization();

        group.MapPost("/", (ClaimTeamCommand command, ISquadsRepository squads, CancellationToken ct) => squads.ClaimTeamAsync(command.TeamId, ct))
            .WithName("ClaimTeam")
            .WithSummary("Crear la plantilla de un equipo")
            .WithDescription("Solo cuentas del cuerpo técnico. Crea la plantilla del equipo para la temporada en curso con el técnico como primer entrenador, importando la plantilla publicada por el proveedor si existe. Nace privada y con código de invitación. Solo puede haber una plantilla por equipo y temporada.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/join", (JoinSquadStaffRequest request, ISquadsRepository squads, CancellationToken ct) => squads.JoinStaffAsync(request, ct))
            .WithName("JoinSquadStaff")
            .WithSummary("Incorporarse al cuerpo técnico de una plantilla")
            .WithDescription("Con el código de invitación que facilita el cuerpo técnico actual.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/mine", (ISquadsRepository squads, CancellationToken ct) => squads.ListMineAsync(ct))
            .WithName("ListMySquads")
            .WithSummary("Mis plantillas")
            .WithDescription("Plantillas en cuyo cuerpo técnico está el técnico autenticado.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{squadId:guid}", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquad")
            .WithSummary("Detalle de una plantilla")
            .WithDescription("Staff y jugadores. Visible para su cuerpo técnico, sus jugadores vinculados y, si es pública, para cualquiera. El código de invitación solo se devuelve al cuerpo técnico.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/players", (Guid squadId, SquadPlayerRequest request, ISquadsRepository squads, CancellationToken ct) => squads.AddPlayerAsync(squadId, request, ct))
            .WithName("AddSquadPlayer")
            .WithSummary("Añadir un jugador a la plantilla")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{squadId:guid}/players/{squadPlayerId:guid}", (Guid squadId, Guid squadPlayerId, SquadPlayerRequest request, ISquadsRepository squads, CancellationToken ct) => squads.UpdatePlayerAsync(squadId, squadPlayerId, request, ct))
            .WithName("UpdateSquadPlayer")
            .WithSummary("Modificar una ficha de la plantilla")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{squadId:guid}/players/{squadPlayerId:guid}", (Guid squadId, Guid squadPlayerId, ISquadsRepository squads, CancellationToken ct) => squads.RemovePlayerAsync(squadId, squadPlayerId, ct))
            .WithName("RemoveSquadPlayer")
            .WithSummary("Dar de baja una ficha")
            .WithDescription("La ficha deja de estar activa pero conserva sus actas.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/players/import", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ImportRosterAsync(squadId, ct))
            .WithName("ImportSquadRoster")
            .WithSummary("Importar la plantilla publicada por el proveedor")
            .WithDescription("Añade las fichas de futbolasturiano.es que aún no estén en la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{squadId:guid}/visibility", (Guid squadId, SquadVisibilityRequest request, ISquadsRepository squads, CancellationToken ct) => squads.SetVisibilityAsync(squadId, request, ct))
            .WithName("SetSquadVisibility")
            .WithSummary("Cambiar la visibilidad de la plantilla")
            .WithDescription("Private: solo cuerpo técnico y jugadores vinculados ven actas y estadísticas. Public: cualquiera.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{squadId:guid}/invite-code", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.RotateInviteCodeAsync(squadId, ct))
            .WithName("RotateSquadInviteCode")
            .WithSummary("Generar un nuevo código de invitación")
            .WithDescription("El código anterior deja de valer.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{squadId:guid}/matches", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListMatchesAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("ListSquadMatches")
            .WithSummary("Partidos de la plantilla")
            .WithDescription("Calendario y resultados del equipo con el número de líneas de acta registradas en cada partido. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/sheet", (Guid squadId, Guid matchId, ISquadsRepository squads, CancellationToken ct) => squads.GetMatchSheetAsync(squadId, matchId, ct))
            .AllowAnonymous()
            .WithName("GetMatchSheet")
            .WithSummary("Acta de un partido")
            .WithDescription("Una línea por ficha de la plantilla con su rendimiento (null si no jugó o no está rellenado). canEdit indica si el usuario puede registrarla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{squadId:guid}/matches/{matchId:guid}/sheet", (Guid squadId, Guid matchId, MatchSheetRequest request, ISquadsRepository squads, CancellationToken ct) => squads.RecordMatchSheetAsync(squadId, matchId, request, ct))
            .WithName("RecordMatchSheet")
            .WithSummary("Registrar el acta de un partido")
            .WithDescription("Solo el cuerpo técnico y solo para partidos disputados. Idempotente: cada línea se crea o se sobrescribe; con played = false se borra. Reglas por línea: minutos 0-120, tiros a puerta ≤ tiros, dos amarillas ⇒ roja, sin minutos no hay acciones, valoración 1-10.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/stats", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetStatsAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquadStats")
            .WithSummary("Estadísticas de la plantilla")
            .WithDescription("Totales de temporada de cada jugador (minutos, goles, asistencias, valoración media, por 90'...) y su impacto en los resultados del equipo. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
