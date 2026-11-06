using TercerTiempo.Hosting;
using TercerTiempo.Application.Squads.TeamStats;
using TercerTiempo.Repository.Squads;
using TercerTiempo.Repository.Videos;

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
            .WithDescription("Staff y jugadores. Solo para su club, su cuerpo técnico, sus jugadores vinculados y el administrador (aunque sea pública). Los códigos personales solo se devuelven al club y al cuerpo técnico vigente.")
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
            .WithDescription("Lo interno (actas internas, estadísticas y vídeos) solo lo ven el club, el cuerpo técnico y los jugadores vinculados. Private: nadie más puede pedirlo. Public: cualquier cuenta puede solicitar al club el acceso a un partido; nada se ve hasta que el club aprueba y la persona canjea su pase.")
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

        group.MapPut("/{squadId:guid}/matches/{matchId:guid}/team-stats", (Guid squadId, Guid matchId, TeamMatchStatsInput request, ISquadsRepository squads, CancellationToken ct) => squads.RecordTeamStatsAsync(squadId, matchId, request, ct))
            .WithName("RecordTeamMatchStats")
            .WithSummary("Registrar los datos del equipo en un partido")
            .WithDescription("Solo el cuerpo técnico y solo para partidos disputados. Datos colectivos que no trae el acta pública, desde el punto de vista del equipo (For = a favor, Against = en contra): posesión, córners, tiros y tiros a puerta, fueras de juego, faltas cometidas y recibidas, goles a balón parado y notas. Se crea o se sobrescribe. Tiros a puerta ≤ tiros; goles a balón parado ≤ goles del partido.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/video-analysis", (Guid squadId, Guid matchId, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.GetAsync(squadId, matchId, ct))
            .WithName("GetMatchVideoAnalysis")
            .WithSummary("Propuesta de acta interna que sale del vídeo")
            .WithDescription("Club propietario, cuerpo técnico vigente y el administrador (solo lectura). Un elemento por vídeo de la plantilla en el partido con el estado de su análisis (NotRequested, WaitingForVideo, Pending, Running con su %, Ready o Failed con el motivo), la calidad (fotogramas con balón y con el campo calibrado), los datos de los dos equipos que ha separado el analizador (él no sabe cuál es el del club) y los jugadores seguidos con la URL de su recorte y lo que propone para su línea. El análisis lo hace el servicio de vídeo al terminar la compresión.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video-analysis", (Guid squadId, Guid matchId, RequestVideoAnalysisRequest? request, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.RequestAsync(squadId, matchId, request, ct))
            .WithName("RequestMatchVideoAnalysis")
            .WithSummary("Analizar (o volver a analizar) el vídeo del partido")
            .WithDescription("Club propietario o cuerpo técnico vigente. Pone en la cola de análisis el vídeo indicado (o todos los de la plantilla en el partido) y olvida la propuesta anterior. 409 si el análisis está desactivado en el servidor (Analysis:Enabled), si el vídeo todavía se está comprimiendo o si ya está en cola o analizándose. El cuerpo es opcional.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video-analysis/apply", (Guid squadId, Guid matchId, ApplyVideoAnalysisRequest request, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.ApplyAsync(squadId, matchId, request, ct))
            .WithName("ApplyMatchVideoAnalysis")
            .WithSummary("Volcar al acta interna lo aceptado de la propuesta del vídeo")
            .WithDescription("Club propietario o cuerpo técnico vigente, y solo en partidos disputados. Escribe por el mismo camino que PUT …/sheet y PUT …/team-stats, en nombre de quien lo vuelca: los datos de equipo marcados en teamFields (el valor del equipo y el del rival) y, por cada jugador seguido asignado a una ficha, sus tiros, tiros a puerta, paradas, recuperaciones, pérdidas y pases (sumando los tramos de un mismo jugador y las dos partes del partido). Un cero de la propuesta nunca borra lo apuntado y los goles solo se apuntan a quien no tenga ninguno. Si cambiaría un valor que ya está en el acta responde 409 (video_analysis.overwrite) con la lista de cambios, y hay que repetir la llamada con overwrite = true. Una línea nueva se crea con los minutos que se ha visto al jugador en el vídeo. Devuelve el acta y la propuesta ya actualizadas.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/video-analysis/crops/{tracklet:int}", async (Guid squadId, Guid matchId, int tracklet, Guid? videoId, HttpContext http, IVideoAnalysisRepository analysis, CancellationToken ct) =>
            {
                var crop = await analysis.GetCropAsync(squadId, matchId, tracklet, videoId, ct);
                http.Response.Headers.CacheControl = "private, max-age=86400";
                return Results.File(crop.Content, crop.ContentType);
            })
            .WithName("GetMatchVideoAnalysisCrop")
            .WithSummary("Recorte de un jugador seguido en el vídeo")
            .WithDescription("Club propietario, cuerpo técnico vigente y el administrador. JPEG con el mejor recorte del jugador seguido, para reconocerlo y asignarlo a una ficha. videoId indica el vídeo cuando el partido tiene dos partes.")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/sheet/comparison", (Guid squadId, Guid matchId, bool? refresh, ISquadsRepository squads, CancellationToken ct) => squads.CompareMatchSheetAsync(squadId, matchId, refresh ?? false, ct))
            .AllowAnonymous()
            .WithName("CompareMatchSheet")
            .WithSummary("Comparador: acta interna frente al acta oficial")
            .WithDescription("Para cada ficha de la plantilla, la línea registrada por el cuerpo técnico y la línea del acta oficial guardada en SQL (emparejadas por nombre y dorsal), con las discrepancias en played, started, minutes, goals, yellowCards y redCard; además, las líneas oficiales sin ficha en la plantilla. Si faltan datos, encola su descarga con los límites configurados. refresh se conserva por compatibilidad y no fuerza llamadas a la federación. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/injury-types", (ISquadsRepository squads, CancellationToken ct) => squads.ListInjuryTypesAsync(ct))
            .WithName("ListInjuryTypes")
            .WithSummary("Tipos de lesión con su duración media orientativa")
            .WithDescription("Catálogo fijo: el frontal propone la vuelta prevista sumando averageDays a la fecha de inicio.");

        group.MapGet("/{squadId:guid}/injuries", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListInjuriesAsync(squadId, ct))
            .WithName("ListSquadInjuries")
            .WithSummary("Lesiones de la plantilla (abiertas primero)")
            .WithDescription("Cuerpo técnico y jugadores vinculados; nadie de fuera las ve, ni con un pase.")
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

        group.MapGet("/{squadId:guid}/season-table", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetSeasonTableAsync(squadId, ct))
            .WithName("GetSquadSeasonTable")
            .WithSummary("Temporada de cada jugador (tabla de la plantilla)")
            .WithDescription("Partidos, titularidades, minutos, goles, asistencias, tarjetas, faltas y nota media de cada ficha activa. Por partido cuenta el acta interna si existe y, si no, el acta oficial descargada de la federación (sin asistencias ni faltas). Indica cuántos partidos salen de cada fuente.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/stats", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetStatsAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquadStats")
            .WithSummary("Estadísticas de la plantilla")
            .WithDescription("Totales de temporada de cada jugador (minutos, goles, asistencias, nota media, por 90'...) y su impacto en los resultados del equipo. Misma visibilidad que la plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/team-stats", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetTeamStatsAsync(squadId, ct))
            .AllowAnonymous()
            .WithName("GetSquadTeamStats")
            .WithSummary("Datos del equipo en la temporada")
            .WithDescription("Partido a partido y en media: posesión, córners, tiros, fueras de juego, faltas y goles a balón parado que ha apuntado el cuerpo técnico. Misma visibilidad que la plantilla.")
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
