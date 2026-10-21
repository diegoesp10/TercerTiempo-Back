using Microsoft.AspNetCore.Http.Features;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Videos;
using Microsoft.Extensions.Options;
using TercerTiempo.Repository.Squads;

namespace TercerTiempo.Api.Endpoints;

internal static class SquadEndpoints
{
    public static IEndpointRouteBuilder MapSquadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/squads").WithTags("Plantillas").RequireAuthorization();

        group.MapGet("/mine", (ISquadsRepository squads, CancellationToken ct) => squads.ListMineAsync(ct))
            .WithName("ListMySquads")
            .WithSummary("Mis plantillas")
            .WithDescription("Plantillas en cuyo cuerpo técnico vigente está el técnico autenticado (nombramientos del club sin caducar).")
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
            .WithDescription("Solo el cuerpo técnico y solo para partidos disputados. Idempotente: cada línea se crea o se sobrescribe; con played = false se borra. Reglas por línea: minutos 0-120, tiros a puerta ≤ tiros, dos amarillas ⇒ roja, sin minutos no hay acciones. La nota no se envía: se calcula con los datos de la línea, la posición del jugador y el resultado (base 6,0 ± ataque, creación, defensa, portería, disciplina y resultado; 3,0-10,0).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/sheet/comparison", (Guid squadId, Guid matchId, bool? refresh, ISquadsRepository squads, CancellationToken ct) => squads.CompareMatchSheetAsync(squadId, matchId, refresh ?? false, ct))
            .AllowAnonymous()
            .WithName("CompareMatchSheet")
            .WithSummary("Comparador: acta interna frente al acta oficial")
            .WithDescription("Para cada ficha de la plantilla, la línea registrada por el cuerpo técnico y la línea del acta de la federación (emparejadas por nombre y dorsal), con las discrepancias en played, started, minutes, goals, yellowCards y redCard; además, las líneas oficiales sin ficha en la plantilla. Consulta (y guarda) el acta oficial si toca; official.status explica si aún no está. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video", async (Guid squadId, Guid matchId, IFormFile file, string? title, HttpContext http, ISquadsRepository squads, IOptions<VideoOptions> options, CancellationToken ct) =>
            {
                // El límite por defecto de Kestrel (30 MB) se queda corto para un partido entero.
                if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.Value.MaxSizeBytes + 1024 * 1024;

                await using var content = file.OpenReadStream();
                return await squads.UploadVideoAsync(squadId, matchId, title, new VideoUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadMatchVideo")
            .WithSummary("Subir el vídeo de un partido")
            .WithDescription("Solo el cuerpo técnico. multipart/form-data con el campo file (mp4, webm, mov o mkv hasta Videos:MaxSizeMb) y, opcionalmente, title. La plantilla que lo sube (staff y jugadores vinculados) lo ve gratis; los técnicos de otras plantillas pueden comprar el acceso al precio por defecto (Videos:DefaultPriceCents).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{squadId:guid}/stats", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetStatsAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquadStats")
            .WithSummary("Estadísticas de la plantilla")
            .WithDescription("Totales de temporada de cada jugador (minutos, goles, asistencias, nota media, por 90'...) y su impacto en los resultados del equipo. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/players/{squadPlayerId:guid}/profile", (Guid squadId, Guid squadPlayerId, ISquadsRepository squads, CancellationToken ct) => squads.GetPlayerProfileAsync(squadId, squadPlayerId, ct))
            .AllowAnonymous()
            .WithName("GetSquadPlayerProfile")
            .WithSummary("Ficha de un jugador para gráficos")
            .WithDescription("Todos los partidos disputados por el equipo con la línea del acta del jugador (si jugó) y el desglose de su nota (ataque, creación, defensa, portería, disciplina, resultado), su perfil por 90' y la media y el máximo de la plantilla en cada eje (jugadores con 90' o más) para escalar el radar. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/timeline", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetTimelineAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquadTimeline")
            .WithSummary("Evolución del equipo")
            .WithDescription("Partidos disputados en orden cronológico con goles a favor y en contra, resultado, puntos acumulados y, si hay acta, nota media, jugadores utilizados y mejor nota. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
