using TercerTiempo.Api.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Videos;
using TercerTiempo.Domain.Videos;
using Microsoft.Extensions.Options;
using TercerTiempo.Repository.Squads;

namespace TercerTiempo.Api.Endpoints;

internal static class SquadEndpoints
{
    public static IEndpointRouteBuilder MapSquadEndpoints(this IEndpointRouteBuilder app)
    {
        // Toda operación sobre una plantilla concreta pasa por la puerta de licencias del equipo.
        var group = app.MapGroup("/api/squads").WithTags("Plantillas").RequireAuthorization().RequireLicense(LicenseScope.Squad);

        group.MapGet("/mine", (ISquadsRepository squads, CancellationToken ct) => squads.ListMineAsync(ct))
            .WithName("ListMySquads")
            .WithSummary("Mis plantillas")
            .WithDescription("Plantillas en cuyo cuerpo técnico vigente está el técnico autenticado (nombramientos del club sin caducar).")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{squadId:guid}", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquad")
            .WithSummary("Detalle de una plantilla")
            .WithDescription("Staff y jugadores. Visible para su club, cuerpo técnico, jugadores vinculados y, si es pública, para cualquiera. Los códigos personales solo se devuelven al club y al cuerpo técnico vigente.")
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

        group.MapPost("/{squadId:guid}/players/{squadPlayerId:guid}/invite-code", (Guid squadId, Guid squadPlayerId, ISquadsRepository squads, CancellationToken ct) => squads.GeneratePlayerInviteCodeAsync(squadId, squadPlayerId, ct))
            .WithName("GeneratePlayerInviteCode")
            .WithSummary("Generar un código personal para un jugador")
            .WithDescription("Solo el club propietario o el cuerpo técnico vigente. La ficha debe estar activa y sin vincular. El código es de un solo uso, exige el mismo nombre y apellidos e invalida el anterior de ese jugador.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
            .WithDescription("Para cada ficha de la plantilla, la línea registrada por el cuerpo técnico y la línea del acta oficial guardada en SQL (emparejadas por nombre y dorsal), con las discrepancias en played, started, minutes, goals, yellowCards y redCard; además, las líneas oficiales sin ficha en la plantilla. Si faltan datos, encola su descarga con los límites configurados. refresh se conserva por compatibilidad y no fuerza llamadas a la federación. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video", async (Guid squadId, Guid matchId, IFormFile file, string? title, VideoPart? part, HttpContext http, ISquadsRepository squads, IOptions<VideoOptions> options, CancellationToken ct) =>
            {
                // El límite por defecto de Kestrel (30 MB) se queda corto para un partido entero.
                if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.Value.MaxSizeBytes + 1024 * 1024;

                await using var content = file.OpenReadStream();
                return await squads.UploadVideoAsync(squadId, matchId, part ?? VideoPart.Full, title, new VideoUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadMatchVideo")
            .WithSummary("Subir el vídeo de un partido de una sola vez")
            .WithDescription("Solo el cuerpo técnico. multipart/form-data con el campo file (mp4 o webm hasta Videos:MaxSizeMb), part (Full, FirstHalf o SecondHalf; Full por defecto) y title opcional. Para archivos grandes usa la subida por trozos (…/video/uploads). La plantilla que lo sube lo ve gratis; los técnicos de otras plantillas compran el acceso al partido (todas sus partes) al precio por defecto (Videos:DefaultPriceCents).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video/uploads", (Guid squadId, Guid matchId, BeginVideoUploadRequest request, ISquadsRepository squads, CancellationToken ct) => squads.BeginVideoUploadAsync(squadId, matchId, request, ct))
            .WithName("BeginVideoUpload")
            .WithSummary("Empezar una subida de vídeo por trozos")
            .WithDescription("Solo el cuerpo técnico. Declara parte, archivo, tipo (mp4 o webm) y tamaño (hasta Videos:MaxSizeMb). Devuelve el id de la subida y el tamaño de trozo (Videos:ChunkSizeMb); los trozos se envían en orden a PUT …/video-uploads/{uploadId}/chunks/{index} y al final se llama a …/complete. La sesión caduca a las Videos:UploadExpiryHours horas.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/video-uploads/{uploadId:guid}", (Guid squadId, Guid uploadId, ISquadsRepository squads, CancellationToken ct) => squads.GetVideoUploadAsync(squadId, uploadId, ct))
            .WithName("GetVideoUpload")
            .WithSummary("Estado de una subida por trozos (para reanudarla)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{squadId:guid}/video-uploads/{uploadId:guid}/chunks/{index:int}", async (Guid squadId, Guid uploadId, int index, HttpContext http, ISquadsRepository squads, IOptions<VideoOptions> options, CancellationToken ct) =>
            {
                if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = options.Value.ChunkSizeBytes + 1024 * 1024;

                var length = http.Request.ContentLength ?? 0;
                return await squads.AppendVideoChunkAsync(squadId, uploadId, index, length, http.Request.Body, ct);
            })
            .Accepts<byte[]>("application/octet-stream")
            .WithName("AppendVideoChunk")
            .WithSummary("Enviar un trozo del vídeo")
            .WithDescription("Cuerpo binario (application/octet-stream) con Content-Length. Los trozos van en orden y todos miden Videos:ChunkSizeMb salvo el último. Devuelve los bytes recibidos y el siguiente índice esperado.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/video-uploads/{uploadId:guid}/complete", (Guid squadId, Guid uploadId, ISquadsRepository squads, CancellationToken ct) => squads.CompleteVideoUploadAsync(squadId, uploadId, ct))
            .WithName("CompleteVideoUpload")
            .WithSummary("Cerrar la subida y publicar el vídeo")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{squadId:guid}/video-uploads/{uploadId:guid}", (Guid squadId, Guid uploadId, ISquadsRepository squads, CancellationToken ct) => squads.CancelVideoUploadAsync(squadId, uploadId, ct))
            .WithName("CancelVideoUpload")
            .WithSummary("Cancelar una subida y borrar el temporal")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/injury-types", (ISquadsRepository squads, CancellationToken ct) => squads.ListInjuryTypesAsync(ct))
            .WithName("ListInjuryTypes")
            .WithSummary("Tipos de lesión con su duración media orientativa")
            .WithDescription("Catálogo fijo: el frontal propone la vuelta prevista sumando averageDays a la fecha de inicio.");

        group.MapGet("/{squadId:guid}/injuries", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListInjuriesAsync(squadId, ct))
            .WithName("ListSquadInjuries")
            .WithSummary("Lesiones de la plantilla (abiertas primero)")
            .WithDescription("Cuerpo técnico y jugadores vinculados; el público no las ve aunque la plantilla sea pública.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/injuries", (Guid squadId, InjuryRequest request, ISquadsRepository squads, CancellationToken ct) => squads.RecordInjuryAsync(squadId, request, ct))
            .WithName("RecordInjury")
            .WithSummary("Poner a un jugador en estado de lesionado")
            .WithDescription("Solo cuerpo técnico. Un jugador solo puede tener una lesión abierta. Si no se indica la vuelta prevista se calcula con la duración media del tipo. Devuelve todas las lesiones de la plantilla.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{squadId:guid}/injuries/{injuryId:guid}", (Guid squadId, Guid injuryId, InjuryRequest request, ISquadsRepository squads, CancellationToken ct) => squads.UpdateInjuryAsync(squadId, injuryId, request, ct))
            .WithName("UpdateInjury")
            .WithSummary("Corregir una lesión")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/injuries/{injuryId:guid}/close", (Guid squadId, Guid injuryId, CloseInjuryRequest request, ISquadsRepository squads, CancellationToken ct) => squads.CloseInjuryAsync(squadId, injuryId, request, ct))
            .WithName("CloseInjury")
            .WithSummary("Dar el alta")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/injuries/{injuryId:guid}/reopen", (Guid squadId, Guid injuryId, ISquadsRepository squads, CancellationToken ct) => squads.ReopenInjuryAsync(squadId, injuryId, ct))
            .WithName("ReopenInjury")
            .WithSummary("Anular el alta (recaída)")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{squadId:guid}/injuries/{injuryId:guid}", (Guid squadId, Guid injuryId, ISquadsRepository squads, CancellationToken ct) => squads.DeleteInjuryAsync(squadId, injuryId, ct))
            .WithName("DeleteInjury")
            .WithSummary("Borrar una lesión registrada por error")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/suggestions", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListSuggestionsAsync(squadId, ct))
            .WithName("ListSquadSuggestions")
            .WithSummary("Sugerencias de los jugadores sobre sus actas")
            .WithDescription("Club propietario o cuerpo técnico vigente. Cada sugerencia trae la línea propuesta, la actual del acta (si existe) y el mensaje del jugador; pendientes primero.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/suggestions/{suggestionId:guid}/resolve", (Guid squadId, Guid suggestionId, ResolveSuggestionRequest request, ISquadsRepository squads, CancellationToken ct) => squads.ResolveSuggestionAsync(squadId, suggestionId, request, ct))
            .WithName("ResolveSuggestion")
            .WithSummary("Aprobar o rechazar una sugerencia")
            .WithDescription("Al aprobar, la línea propuesta se escribe en el acta del partido (se crea o se actualiza) en nombre del técnico que aprueba o, si aprueba el club, del primer entrenador vigente. Devuelve la lista actualizada.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
