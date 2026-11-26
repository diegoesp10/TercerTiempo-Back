using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Domain.Squads;
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
            .WithDescription("La ficha pasa a «De baja» (leftPlayers del detalle) con todo lo que hizo antes: actas, asistencia, convocatorias y lesiones. Desde ese día no entra en convocatorias, entrenamientos ni actas nuevas, no se edita, y sus avisos abiertos de entrenamientos y convocatorias se cierran.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/players/{squadPlayerId:guid}/reinstate", (Guid squadId, Guid squadPlayerId, ISquadsRepository squads, CancellationToken ct) => squads.ReinstatePlayerAsync(squadId, squadPlayerId, ct))
            .WithName("ReinstateSquadPlayer")
            .WithSummary("Reincorporar una ficha dada de baja")
            .WithDescription("Vuelve a la plantilla con su historial y, si tenía la cuenta vinculada, el jugador recupera el acceso. 422 si no estaba de baja o ya hay otra ficha activa de esa persona; 409 si con su cuenta juega ahora en otra plantilla.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/players/import", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ImportRosterAsync(squadId, ct))
            .WithName("ImportSquadRoster")
            .WithSummary("Importar la plantilla publicada por el proveedor")
            .WithDescription("Añade las fichas de futbolasturiano.es que aún no estén en la plantilla. Las que causaron baja no vuelven: se reincorporan a mano.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{squadId:guid}/visibility", (Guid squadId, SquadVisibilityRequest request, ISquadsRepository squads, CancellationToken ct) => squads.SetVisibilityAsync(squadId, request, ct))
            .WithName("SetSquadVisibility")
            .WithSummary("Cambiar la visibilidad de la plantilla")
            .WithDescription("Lo interno (actas internas, estadísticas y vídeos) solo lo ven el club, el cuerpo técnico y los jugadores vinculados. Private: nadie más puede pedirlo. Public: cualquier cuenta puede solicitar al club el acceso a un partido; nada se ve hasta que el club aprueba y la persona canjea su pase.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{squadId:guid}/call-up-size", (Guid squadId, SquadCallUpSizeRequest request, ISquadsRepository squads, CancellationToken ct) => squads.SetCallUpSizeAsync(squadId, request, ct))
            .WithName("SetSquadCallUpSize")
            .WithSummary("Fijar a cuántos se convoca")
            .WithDescription("Con un número (de 7 a 30), todas las convocatorias son de ese número exacto: no se puede marcar a más y, si se envía con menos, el cuerpo técnico y el club reciben un aviso de que faltan jugadores por convocar. Sin número (null), se pregunta en cada convocatoria.")
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
            .WithDescription("Club propietario, cuerpo técnico vigente y el administrador (solo lectura). Un elemento por vídeo de la plantilla en el partido con el estado de su análisis (NotRequested, WaitingForVideo, Pending, Running con su %, Ready, Failed con el motivo o Cancelled), la calidad (fotogramas con balón y con el campo calibrado), los datos de los dos equipos que ha separado el analizador (él no sabe cuál es el del club) y los jugadores seguidos con la URL de su recorte y lo que propone para su línea. El análisis lo hace el analizador que esté en marcha al terminar la compresión; analyzerOnline dice si hay alguno encendido (si no, lo que está en cola no avanza).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/video-analysis/stamp", (Guid squadId, Guid matchId, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.GetStampAsync(squadId, matchId, ct))
            .WithName("GetMatchVideoAnalysisStamp")
            .WithSummary("Huella del análisis del vídeo, para seguirlo en directo")
            .WithDescription("Muy ligera (sin la propuesta, el acta oficial ni los recortes): cambia en cuanto cambia algo de lo que enseña el análisis (estado y avance, también de la compresión del vídeo, lo que cuenta en directo, el registro, las pausas, lo volcado, si hay analizador). Si no coincide con el stamp del análisis que se tiene, se vuelve a pedir entero. Mismos permisos que el análisis.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video-analysis", (Guid squadId, Guid matchId, RequestVideoAnalysisRequest? request, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.RequestAsync(squadId, matchId, request, ct))
            .WithName("RequestMatchVideoAnalysis")
            .WithSummary("Analizar (o volver a analizar) el vídeo del partido")
            .WithDescription("Club propietario o cuerpo técnico vigente. Pone en la cola de análisis el vídeo indicado (o todos los de la plantilla en el partido) y olvida la propuesta anterior. La cola la atiende el analizador que esté en marcha (el del servicio de vídeo, o uno aparte con GPU con Start-Analyzer.ps1): tarda unos dos tercios de lo que dura el vídeo. 409 si no se admiten análisis en este servidor (Analysis:Enabled), si el vídeo todavía se está comprimiendo o si ya está en cola o analizándose. El cuerpo es opcional.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video-analysis/cancel", (Guid squadId, Guid matchId, CancelVideoAnalysisRequest? request, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.CancelAsync(squadId, matchId, request, ct))
            .WithName("CancelMatchVideoAnalysis")
            .WithSummary("Cancelar el análisis del vídeo del partido")
            .WithDescription("Club propietario o cuerpo técnico vigente. Para lo que está en cola o analizándose del vídeo indicado (o de todos los de la plantilla en el partido): queda como Cancelled y se puede volver a pedir. También lo que está en pausa. El analizador lo nota en unos segundos, para el proceso de Python y no guarda nada. 409 (video_analysis.not_active) si no hay nada en cola, analizándose ni en pausa. El cuerpo es opcional.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video-analysis/pause", (Guid squadId, Guid matchId, CancelVideoAnalysisRequest? request, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.PauseAsync(squadId, matchId, request, true, ct))
            .WithName("PauseMatchVideoAnalysis")
            .WithSummary("Pausar el análisis del vídeo del partido")
            .WithDescription("Club propietario o cuerpo técnico vigente. Lo que está en cola no se empieza y lo que se está analizando espera donde va, sin perder nada (el analizador lo nota en unos segundos y para en el siguiente fotograma; la GPU deja de trabajar, pero la cola espera detrás). Queda como Paused. 422 (video_analysis.not_pausable) si no hay nada en cola ni analizándose. El cuerpo es opcional.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/matches/{matchId:guid}/video-analysis/resume", (Guid squadId, Guid matchId, CancelVideoAnalysisRequest? request, IVideoAnalysisRepository analysis, CancellationToken ct) => analysis.PauseAsync(squadId, matchId, request, false, ct))
            .WithName("ResumeMatchVideoAnalysis")
            .WithSummary("Reanudar el análisis del vídeo del partido")
            .WithDescription("Club propietario o cuerpo técnico vigente. Sigue donde se quedó: lo que se estaba analizando continúa desde el mismo fotograma y lo que se pausó en cola vuelve a la cola. 422 (video_analysis.not_paused) si no hay nada en pausa. El cuerpo es opcional.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
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
            .WithDescription("El catálogo que mantiene el administrador. Los retirados (active = false) salen marcados: no se ofrecen para lesiones nuevas, pero sí para corregir las que ya los tienen. El frontal propone la vuelta prevista sumando averageDays a la fecha de inicio. Cada uno dice lo que se pregunta de la lesión: un sitio fijo (baseSite, el tobillo en el esguince de tobillo) con lo que lo precisa si se sabe (sites), o una clase de sitio (siteKind: qué músculo, qué hueso...) con todos los de esa clase o solo los de sites; y si lleva grado (graded).");

        group.MapGet("/body-sites", (ISquadsRepository squads, CancellationToken ct) => squads.ListBodySitesAsync(ct))
            .WithName("ListBodySites")
            .WithSummary("Catálogo de sitios del cuerpo para las lesiones")
            .WithDescription("Músculos (con su grupo: el bíceps femoral en los isquiotibiales), huesos, tendones, ligamentos, articulaciones, cartílagos y zonas, de la cabeza a los pies, con si tienen lado (paired) y cómo concuerda el lado con su nombre (agreement). Salen también los retirados (active = false): no se ofrecen para lesiones nuevas, pero las que ya los tienen los enseñan.");

        group.MapGet("/{squadId:guid}/injuries", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListInjuriesAsync(squadId, ct))
            .WithName("ListSquadInjuries")
            .WithSummary("Lesiones de la plantilla (abiertas primero)")
            .WithDescription("Cuerpo técnico y jugadores vinculados; nadie de fuera las ve, ni con un pase.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/injuries", (Guid squadId, InjuryRequest request, ISquadsRepository squads, CancellationToken ct) => squads.RecordInjuryAsync(squadId, request, ct))
            .WithName("RecordInjury")
            .WithSummary("Poner a un jugador en estado de lesionado")
            .WithDescription("Solo cuerpo técnico. Un jugador solo puede tener una lesión abierta. Si no se indica la vuelta prevista se calcula con la duración media del tipo. Con matchId o trainingSessionId queda dicho dónde se lesionó (el parte, de ese día o de después); si es un partido, la posible lesión de su crónica para ese jugador queda registrada con ella. Devuelve todas las lesiones de la plantilla.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{squadId:guid}/injuries/{injuryId:guid}", (Guid squadId, Guid injuryId, InjuryRequest request, ISquadsRepository squads, CancellationToken ct) => squads.UpdateInjuryAsync(squadId, injuryId, request, ct))
            .WithName("UpdateInjury")
            .WithSummary("Corregir una lesión")
            .WithDescription("También dónde se lesionó (matchId, trainingSessionId o ninguno): enlazarla a un partido registra la posible lesión de su crónica para ese jugador, y llevarla a otro sitio descarta la que se registró con ella.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/injuries/{injuryId:guid}/close", (Guid squadId, Guid injuryId, CloseInjuryRequest request, ISquadsRepository squads, CancellationToken ct) => squads.CloseInjuryAsync(squadId, injuryId, request, ct))
            .WithName("CloseInjury")
            .WithSummary("Dar el alta")
            .WithDescription("Con el día del alta real (por defecto hoy; nunca posterior a hoy ni anterior al parte): de ahí salen los días que tardó en recuperarse.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{squadId:guid}/injuries/{injuryId:guid}/reopen", (Guid squadId, Guid injuryId, ISquadsRepository squads, CancellationToken ct) => squads.ReopenInjuryAsync(squadId, injuryId, ct))
            .WithName("ReopenInjury")
            .WithSummary("Deshacer un alta dada por error")
            .WithDescription("La lesión vuelve a estar abierta como si no se hubiera dado el alta. Si la lesión ha vuelto después de un alta de verdad, se registra una recaída (POST …/relapse), que no borra esa alta. 409 injury.has_relapse si ya tiene una recaída detrás.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{squadId:guid}/injuries/{injuryId:guid}/relapse", (Guid squadId, Guid injuryId, RelapseRequest request, ISquadsRepository squads, CancellationToken ct) => squads.RecordRelapseAsync(squadId, injuryId, request, ct))
            .WithName("RecordInjuryRelapse")
            .WithSummary("Registrar una recaída de una lesión superada")
            .WithDescription("Solo cuerpo técnico. Crea una lesión nueva del mismo tipo enlazada a la superada, que conserva su alta y sus días de baja. Devuelve todas las lesiones de la plantilla.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/players/{squadPlayerId:guid}/injuries", (Guid squadId, Guid squadPlayerId, ISquadsRepository squads, CancellationToken ct) => squads.GetPlayerInjuryHistoryAsync(squadId, squadPlayerId, ct))
            .WithName("GetPlayerInjuryHistory")
            .WithSummary("Historial de lesiones de un jugador en el club")
            .WithDescription("Las de esta temporada y las de la misma persona en las plantillas de otras temporadas y equipos del club, con los días del parte al alta. El club, el cuerpo técnico y el propio jugador (403 injury.private para sus compañeros); el administrador, para leer.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{squadId:guid}/injuries/{injuryId:guid}", (Guid squadId, Guid injuryId, ISquadsRepository squads, CancellationToken ct) => squads.DeleteInjuryAsync(squadId, injuryId, ct))
            .WithName("DeleteInjury")
            .WithSummary("Borrar una lesión registrada por error")
            .WithDescription("Solo cuerpo técnico. Se borran también sus pruebas e informes.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/injury-proposals", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListInjuryProposalsAsync(squadId, ct))
            .WithName("ListInjuryProposals")
            .WithSummary("Posibles lesiones sacadas de las crónicas de los partidos")
            .WithDescription("Club, cuerpo técnico y administrador (solo para leer). Las que nadie ha registrado ni descartado, con la frase de la crónica, el minuto en que el jugador dejó el campo según el acta oficial y el tipo de lesión que encaja. Ninguna es una lesión hasta que se registra con POST /injuries y su proposalId.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/injury-proposals/{proposalId:guid}/dismiss", (Guid squadId, Guid proposalId, ISquadsRepository squads, CancellationToken ct) => squads.DismissInjuryProposalAsync(squadId, proposalId, ct))
            .WithName("DismissInjuryProposal")
            .WithSummary("Descartar una posible lesión")
            .WithDescription("Club o cuerpo técnico. No se registra nada y su aviso queda resuelto. Devuelve las posibles lesiones que quedan.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/injuries", (Guid squadId, Guid matchId, ISquadsRepository squads, CancellationToken ct) => squads.ListMatchInjuriesAsync(squadId, matchId, ct))
            .WithName("ListMatchInjuries")
            .WithSummary("Lesiones de la plantilla en un partido")
            .WithDescription("Para los sucesos del partido. Las que se hicieron en él (confirmed), para quien ve las lesiones de la plantilla (un jugador, solo las suyas), y las posibles de la crónica de quien no tiene ya una de ese partido, solo para el club, el cuerpo técnico y el administrador.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/injury-occasions", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.ListInjuryOccasionsAsync(squadId, ct))
            .WithName("ListInjuryOccasions")
            .WithSummary("Partidos y entrenamientos en que se pudo lesionar un jugador")
            .WithDescription("Para el «¿Dónde se lesionó?» del formulario de lesión: los partidos de liga del equipo en esta temporada y los entrenamientos de la plantilla que ya han empezado, los más recientes primero. Club, cuerpo técnico y administrador (solo para leer).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/injuries/{injuryId:guid}/documents", async (
                Guid squadId, Guid injuryId, IFormFile file, [FromForm] InjuryDocumentKind kind, [FromForm] string? note,
                ISquadsRepository squads, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return await squads.UploadInjuryDocumentAsync(squadId, injuryId, kind, note, new CrestUpload(file.FileName, file.ContentType, file.Length, content), ct);
            })
            .DisableAntiforgery()
            .WithName("UploadInjuryDocument")
            .WithSummary("Adjuntar una prueba o un informe a una lesión")
            .WithDescription("Club o cuerpo técnico vigente. multipart/form-data con file (PDF, JPG, PNG o WebP, hasta 15 MB), kind (Imaging, MedicalReport u Other) y note opcional (hasta 200 caracteres). Como mucho 10 por lesión. Devuelve todas las lesiones de la plantilla.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/injuries/{injuryId:guid}/documents/{documentId:guid}", async (
                Guid squadId, Guid injuryId, Guid documentId, HttpContext context, ISquadsRepository squads, CancellationToken ct) =>
            {
                var document = await squads.OpenInjuryDocumentAsync(squadId, injuryId, documentId, ct);
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = document.FileName }.ToString();
                return Results.Stream(document.Content, document.ContentType);
            })
            .WithName("GetInjuryDocument")
            .WithSummary("Abrir una prueba o un informe de una lesión")
            .WithDescription("El club, el cuerpo técnico vigente y el propio jugador; nadie más, tampoco el administrador (403 injury_document.private). No se guarda en caché.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{squadId:guid}/injuries/{injuryId:guid}/documents/{documentId:guid}", (Guid squadId, Guid injuryId, Guid documentId, ISquadsRepository squads, CancellationToken ct) =>
                squads.DeleteInjuryDocumentAsync(squadId, injuryId, documentId, ct))
            .WithName("DeleteInjuryDocument")
            .WithSummary("Quitar una prueba o un informe de una lesión")
            .WithDescription("Club o cuerpo técnico vigente. Borra también el fichero. Devuelve todas las lesiones de la plantilla.")
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
            .WithDescription("Partidos, titularidades, minutos, goles, asistencias, tarjetas, faltas y nota media de cada ficha activa. Por partido cuenta el acta interna si existe y, si no, el acta oficial descargada de la federación (sin asistencias ni faltas). Indica cuántos partidos salen de cada fuente. A quien es de la plantilla le dice además quién sigue lesionado; el detalle de la lesión, solo al club, al cuerpo técnico, al administrador y al propio jugador.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/discipline", (Guid squadId, ISquadsRepository squads, CancellationToken ct) => squads.GetDisciplineAsync(squadId, ct))
            .WithName("GetSquadDiscipline")
            .WithSummary("Apercibidos y sancionados para el próximo partido")
            .WithDescription("Cómo llega cada uno al próximo partido de liga del equipo: los jugadores apercibidos (cuatro amarillas del ciclo de cinco) o sancionados (5.ª amarilla, expulsión o lo que publique el comité de competición), con el motivo, y el cuerpo técnico de la plantilla que figura en las actas oficiales con sus tarjetas de la temporada, apercibido o sancionado igual (también con las inhabilitaciones por días del comité). Se calcula al leer con las actas oficiales cerradas (y, para los jugadores, las internas mientras no llega la oficial). Misma visibilidad que la plantilla.")
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
