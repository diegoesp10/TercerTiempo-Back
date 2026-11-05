using TercerTiempo.Hosting;
using TercerTiempo.Repository.Trainings;

namespace TercerTiempo.Api.Endpoints;

internal static class TrainingEndpoints
{
    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/trainings/catalog", (ITrainingsRepository trainings, CancellationToken ct) => trainings.CatalogAsync(ct))
            .WithTags("Entrenamientos")
            .WithName("GetTrainingCatalog")
            .WithSummary("Tipos de entrenamiento y de ejercicio, y los límites de duración y repetición")
            .WithDescription("Catálogo fijo: el frontal lo usa para los desplegables y los límites de los formularios.");

        MapSquad(app);
        MapPlayer(app);
        return app;
    }

    private static void MapSquad(IEndpointRouteBuilder app)
    {
        // Todo lo de una plantilla pasa por la puerta de licencias del equipo. Lo ven el club, el cuerpo técnico y los
        // jugadores vinculados; lo preparan el club y el cuerpo técnico vigente.
        var group = app.MapGroup("/api/squads").WithTags("Entrenamientos").RequireAuthorization().RequireLicense(LicenseScope.Squad);

        group.MapGet("/{squadId:guid}/exercises", (Guid squadId, ITrainingsRepository trainings, CancellationToken ct) => trainings.ListExercisesAsync(squadId, ct))
            .WithName("ListExercises")
            .WithSummary("Biblioteca de ejercicios de la plantilla")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/exercises", (Guid squadId, ExerciseRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.CreateExerciseAsync(squadId, request, ct))
            .WithName("CreateExercise")
            .WithSummary("Añadir un ejercicio a la biblioteca")
            .WithDescription("Club propietario o cuerpo técnico vigente. Nombre, tipo, duración de referencia, descripción y material. Los entrenamientos guardan su propia copia de cada ejercicio.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{squadId:guid}/exercises/{exerciseId:guid}", (Guid squadId, Guid exerciseId, ExerciseRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.UpdateExerciseAsync(squadId, exerciseId, request, ct))
            .WithName("UpdateExercise")
            .WithSummary("Cambiar un ejercicio de la biblioteca")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{squadId:guid}/exercises/{exerciseId:guid}", (Guid squadId, Guid exerciseId, ITrainingsRepository trainings, CancellationToken ct) => trainings.DeleteExerciseAsync(squadId, exerciseId, ct))
            .WithName("DeleteExercise")
            .WithSummary("Quitar un ejercicio de la biblioteca (las sesiones que lo usaban conservan su copia)")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/trainings", (Guid squadId, DateOnly from, DateOnly to, ITrainingsRepository trainings, CancellationToken ct) => trainings.ListTrainingsAsync(squadId, from, to, ct))
            .WithName("ListTrainings")
            .WithSummary("Entrenamientos entre dos fechas (ambas incluidas)")
            .WithDescription("Para el calendario: cada sesión con su tipo, hora, duración, cuántos ejercicios tiene y el recuento de asistencia. Un jugador vinculado recibe además su propia línea (mine).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{squadId:guid}/trainings", (Guid squadId, TrainingRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.PlanTrainingAsync(squadId, request, ct))
            .WithName("PlanTraining")
            .WithSummary("Preparar un entrenamiento (o una serie semanal)")
            .WithDescription("Club propietario o cuerpo técnico vigente. Tipo, fecha y hora, duración (15-300 min), lugar, notas y ejercicios. Con repeatWeekdays y repeatUntil se crea una sesión por cada día elegido hasta esa fecha (máximo 60), todas con los mismos ejercicios y unidas por seriesId. Toda la plantilla queda como esperada en cada sesión.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{squadId:guid}/trainings/{sessionId:guid}", (Guid squadId, Guid sessionId, ITrainingsRepository trainings, CancellationToken ct) => trainings.GetTrainingAsync(squadId, sessionId, ct))
            .WithName("GetTraining")
            .WithSummary("Detalle de un entrenamiento: ejercicios y asistencia")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{squadId:guid}/trainings/{sessionId:guid}", (Guid squadId, Guid sessionId, TrainingRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.UpdateTrainingAsync(squadId, sessionId, request, ct))
            .WithName("UpdateTraining")
            .WithSummary("Cambiar tipo, fecha, duración, lugar o notas de un entrenamiento")
            .WithDescription("Solo esa sesión (la repetición no se cambia aquí). Los ejercicios van en PUT …/items.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{squadId:guid}/trainings/{sessionId:guid}/items", (Guid squadId, Guid sessionId, TrainingItemsRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.SetTrainingItemsAsync(squadId, sessionId, request, ct))
            .WithName("SetTrainingItems")
            .WithSummary("Guardar los ejercicios de un entrenamiento, en orden")
            .WithDescription("Sustituye la lista entera (hasta 40). Cada ejercicio lleva nombre, tipo y duración; exerciseId enlaza con la biblioteca si salió de ella.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{squadId:guid}/trainings/{sessionId:guid}/attendance", (Guid squadId, Guid sessionId, AttendanceRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.SetAttendanceAsync(squadId, sessionId, request, ct))
            .WithName("SetTrainingAttendance")
            .WithSummary("Marcar quién viene, quién va a faltar, quién vino y quién faltó")
            .WithDescription("Club propietario o cuerpo técnico vigente. Solo las líneas enviadas cambian; el resto se queda como estaba. Motivo opcional en WillMiss y Missed.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{squadId:guid}/trainings/{sessionId:guid}", (Guid squadId, Guid sessionId, bool? series, ITrainingsRepository trainings, CancellationToken ct) => trainings.DeleteTrainingAsync(squadId, sessionId, series ?? false, ct))
            .WithName("DeleteTraining")
            .WithSummary("Borrar un entrenamiento")
            .WithDescription("Con ?series=true se borran también las sesiones posteriores de la misma serie semanal.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/callups", (Guid squadId, ITrainingsRepository trainings, CancellationToken ct) => trainings.ListCallUpsAsync(squadId, ct))
            .WithName("ListSquadCallUps")
            .WithSummary("Estado de las convocatorias de la plantilla, por partido")
            .WithDescription("Para la lista de partidos: borrador o publicada, recuento de convocados y de respuestas y, para un jugador, su línea.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{squadId:guid}/matches/{matchId:guid}/callup", (Guid squadId, Guid matchId, ITrainingsRepository trainings, CancellationToken ct) => trainings.GetCallUpAsync(squadId, matchId, ct))
            .WithName("GetMatchCallUp")
            .WithSummary("Convocatoria de un partido")
            .WithDescription("Toda la plantilla con quién va y qué ha contestado cada uno. El borrador solo lo ve el cuerpo técnico; los jugadores la ven publicada.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{squadId:guid}/matches/{matchId:guid}/callup", (Guid squadId, Guid matchId, CallUpRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.SaveCallUpAsync(squadId, matchId, request, ct))
            .WithName("SaveMatchCallUp")
            .WithSummary("Guardar o publicar la convocatoria de un partido")
            .WithDescription("Club propietario o cuerpo técnico vigente, solo antes del partido. Quién va, cita (hora y lugar) y notas. Al publicar se avisa en la app a cada jugador con cuenta, vaya o no; después, solo a quien le cambie la convocatoria. El convocado confirma o avisa de que no puede; el que no va se da por enterado.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static void MapPlayer(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/players/me").WithTags("Entrenamientos").RequireAuthorization().RequireLicense(LicenseScope.Player);

        group.MapGet("/trainings", (ITrainingsRepository trainings, CancellationToken ct) => trainings.MyTrainingsAsync(ct))
            .WithName("ListMyTrainings")
            .WithSummary("Próximos entrenamientos del jugador, con su asistencia")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/trainings/{sessionId:guid}/absence", (Guid sessionId, AbsenceRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.ReportAbsenceAsync(sessionId, request, ct))
            .WithName("ReportTrainingAbsence")
            .WithSummary("Avisar de que no va a un entrenamiento (o de que al final sí)")
            .WithDescription("Solo antes de que empiece. El cuerpo técnico y el club reciben el aviso en su campanita.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/callups", (ITrainingsRepository trainings, CancellationToken ct) => trainings.MyCallUpsAsync(ct))
            .WithName("ListMyCallUps")
            .WithSummary("Convocatorias publicadas de mi equipo, con mi respuesta")
            .WithDescription("Las de los partidos que quedan y las de la última semana.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/callups/{callUpId:guid}/response", (Guid callUpId, CallUpResponseRequest request, ITrainingsRepository trainings, CancellationToken ct) => trainings.RespondCallUpAsync(callUpId, request, ct))
            .WithName("RespondCallUp")
            .WithSummary("Firmar la convocatoria")
            .WithDescription("Convocado: Confirmed o Declined (con nota opcional). No convocado: Acknowledged. Hasta que empiece el partido; se puede cambiar. Si no puedes ir, el cuerpo técnico y el club reciben un aviso.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
