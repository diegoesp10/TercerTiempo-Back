using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Repository.Trainings;

public sealed record ExerciseRequest(
    string Name,
    [property: Description("Warmup, Technical, Tactical, Physical, Goalkeeping, SetPieces, Game, Cooldown u Other.")]
    ExerciseType Type,
    int DurationMinutes,
    string? Description,
    string? Material);

public sealed record TrainingRequest(
    [property: Description("Season (entrenamiento de la semana), Preseason, Recovery, MatchPreparation u Other.")]
    TrainingKind Kind,
    [property: Description("Opcional; si falta se usa el nombre del tipo.")]
    string? Title,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string? Location,
    string? Notes,
    [property: Description("Ejercicios de la sesión, en orden; vacío para prepararlos después.")]
    IReadOnlyList<TrainingItemInput>? Items,
    [property: Description("Solo al crear: días de la semana en los que se repite (Monday…Sunday) hasta RepeatUntil; vacío = una sola sesión.")]
    IReadOnlyList<DayOfWeek>? RepeatWeekdays,
    DateOnly? RepeatUntil)
{
    internal PlanTrainingCommand ToPlan(Guid squadId) => new(squadId, Kind, Title, StartsAt, DurationMinutes, Location, Notes, Items ?? [], RepeatWeekdays ?? [], RepeatUntil);

    internal UpdateTrainingCommand ToUpdate(Guid squadId, Guid sessionId) => new(squadId, sessionId, Kind, Title, StartsAt, DurationMinutes, Location, Notes);
}

public sealed record TrainingItemsRequest(IReadOnlyList<TrainingItemInput> Items);

public sealed record AttendanceRequest(
    [property: Description("Expected (cuenta con venir), WillMiss (va a faltar), Attended (vino) o Missed (faltó).")]
    IReadOnlyList<AttendanceInput> Entries);

public sealed record AbsenceRequest(
    [property: Description("true: no va; false: al final sí va.")]
    bool WillMiss,
    string? Reason);

public sealed record CallUpRequest(
    IReadOnlyList<Guid> CalledSquadPlayerIds,
    [property: Description("Hora de la cita (quedada), opcional.")]
    DateTimeOffset? MeetAt,
    string? MeetPlace,
    string? Notes,
    [property: Description("true publica y avisa a la plantilla; false deja el borrador (si ya estaba publicada, se vuelve a publicar igualmente).")]
    bool Publish);

public sealed record CallUpResponseRequest(
    [property: Description("Confirmed o Declined si vas convocado; Acknowledged si no vas.")]
    CallUpResponse Response,
    string? Note);

public interface ITrainingsRepository
{
    Task<TrainingCatalogDto> CatalogAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExerciseDto>> ListExercisesAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExerciseDto>> CreateExerciseAsync(Guid squadId, ExerciseRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExerciseDto>> UpdateExerciseAsync(Guid squadId, Guid exerciseId, ExerciseRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExerciseDto>> DeleteExerciseAsync(Guid squadId, Guid exerciseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingSummaryDto>> ListTrainingsAsync(Guid squadId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<TrainingSessionDto> GetTrainingAsync(Guid squadId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrainingSummaryDto>> PlanTrainingAsync(Guid squadId, TrainingRequest request, CancellationToken cancellationToken = default);
    Task<TrainingSessionDto> UpdateTrainingAsync(Guid squadId, Guid sessionId, TrainingRequest request, CancellationToken cancellationToken = default);
    Task<TrainingSessionDto> SetTrainingItemsAsync(Guid squadId, Guid sessionId, TrainingItemsRequest request, CancellationToken cancellationToken = default);
    Task<TrainingSessionDto> SetAttendanceAsync(Guid squadId, Guid sessionId, AttendanceRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteTrainingAsync(Guid squadId, Guid sessionId, bool series, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CallUpSummaryDto>> ListCallUpsAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<MatchCallUpDto> GetCallUpAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<MatchCallUpDto> SaveCallUpAsync(Guid squadId, Guid matchId, CallUpRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyTrainingDto>> MyTrainingsAsync(CancellationToken cancellationToken = default);
    Task<TrainingSessionDto> ReportAbsenceAsync(Guid sessionId, AbsenceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyCallUpDto>> MyCallUpsAsync(CancellationToken cancellationToken = default);
    Task<MyCallUpDto> RespondCallUpAsync(Guid callUpId, CallUpResponseRequest request, CancellationToken cancellationToken = default);
}

internal sealed class TrainingsRepository(
    IQueryHandler<GetTrainingCatalogQuery, TrainingCatalogDto> catalog,
    IQueryHandler<ListExercisesQuery, IReadOnlyList<ExerciseDto>> listExercises,
    ICommandHandler<CreateExerciseCommand, IReadOnlyList<ExerciseDto>> createExercise,
    ICommandHandler<UpdateExerciseCommand, IReadOnlyList<ExerciseDto>> updateExercise,
    ICommandHandler<DeleteExerciseCommand, IReadOnlyList<ExerciseDto>> deleteExercise,
    IQueryHandler<ListTrainingsQuery, IReadOnlyList<TrainingSummaryDto>> listTrainings,
    IQueryHandler<GetTrainingQuery, TrainingSessionDto> getTraining,
    ICommandHandler<PlanTrainingCommand, IReadOnlyList<TrainingSummaryDto>> planTraining,
    ICommandHandler<UpdateTrainingCommand, TrainingSessionDto> updateTraining,
    ICommandHandler<SetTrainingItemsCommand, TrainingSessionDto> setItems,
    ICommandHandler<SetAttendanceCommand, TrainingSessionDto> setAttendance,
    ICommandHandler<DeleteTrainingCommand, bool> deleteTraining,
    IQueryHandler<ListSquadCallUpsQuery, IReadOnlyList<CallUpSummaryDto>> listCallUps,
    IQueryHandler<GetMatchCallUpQuery, MatchCallUpDto> getCallUp,
    ICommandHandler<SaveMatchCallUpCommand, MatchCallUpDto> saveCallUp,
    IQueryHandler<ListMyTrainingsQuery, IReadOnlyList<MyTrainingDto>> myTrainings,
    ICommandHandler<ReportTrainingAbsenceCommand, TrainingSessionDto> reportAbsence,
    IQueryHandler<ListMyCallUpsQuery, IReadOnlyList<MyCallUpDto>> myCallUps,
    ICommandHandler<RespondCallUpCommand, MyCallUpDto> respondCallUp) : ITrainingsRepository
{
    public Task<TrainingCatalogDto> CatalogAsync(CancellationToken cancellationToken = default) =>
        catalog.Handle(new GetTrainingCatalogQuery(), cancellationToken);

    public Task<IReadOnlyList<ExerciseDto>> ListExercisesAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        listExercises.Handle(new ListExercisesQuery(squadId), cancellationToken);

    public Task<IReadOnlyList<ExerciseDto>> CreateExerciseAsync(Guid squadId, ExerciseRequest request, CancellationToken cancellationToken = default) =>
        createExercise.Handle(new CreateExerciseCommand(squadId, request.Name, request.Type, request.DurationMinutes, request.Description, request.Material), cancellationToken);

    public Task<IReadOnlyList<ExerciseDto>> UpdateExerciseAsync(Guid squadId, Guid exerciseId, ExerciseRequest request, CancellationToken cancellationToken = default) =>
        updateExercise.Handle(new UpdateExerciseCommand(squadId, exerciseId, request.Name, request.Type, request.DurationMinutes, request.Description, request.Material), cancellationToken);

    public Task<IReadOnlyList<ExerciseDto>> DeleteExerciseAsync(Guid squadId, Guid exerciseId, CancellationToken cancellationToken = default) =>
        deleteExercise.Handle(new DeleteExerciseCommand(squadId, exerciseId), cancellationToken);

    public Task<IReadOnlyList<TrainingSummaryDto>> ListTrainingsAsync(Guid squadId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        listTrainings.Handle(new ListTrainingsQuery(squadId, from, to), cancellationToken);

    public Task<TrainingSessionDto> GetTrainingAsync(Guid squadId, Guid sessionId, CancellationToken cancellationToken = default) =>
        getTraining.Handle(new GetTrainingQuery(squadId, sessionId), cancellationToken);

    public Task<IReadOnlyList<TrainingSummaryDto>> PlanTrainingAsync(Guid squadId, TrainingRequest request, CancellationToken cancellationToken = default) =>
        planTraining.Handle(request.ToPlan(squadId), cancellationToken);

    public Task<TrainingSessionDto> UpdateTrainingAsync(Guid squadId, Guid sessionId, TrainingRequest request, CancellationToken cancellationToken = default) =>
        updateTraining.Handle(request.ToUpdate(squadId, sessionId), cancellationToken);

    public Task<TrainingSessionDto> SetTrainingItemsAsync(Guid squadId, Guid sessionId, TrainingItemsRequest request, CancellationToken cancellationToken = default) =>
        setItems.Handle(new SetTrainingItemsCommand(squadId, sessionId, request.Items), cancellationToken);

    public Task<TrainingSessionDto> SetAttendanceAsync(Guid squadId, Guid sessionId, AttendanceRequest request, CancellationToken cancellationToken = default) =>
        setAttendance.Handle(new SetAttendanceCommand(squadId, sessionId, request.Entries), cancellationToken);

    public Task<bool> DeleteTrainingAsync(Guid squadId, Guid sessionId, bool series, CancellationToken cancellationToken = default) =>
        deleteTraining.Handle(new DeleteTrainingCommand(squadId, sessionId, series), cancellationToken);

    public Task<IReadOnlyList<CallUpSummaryDto>> ListCallUpsAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        listCallUps.Handle(new ListSquadCallUpsQuery(squadId), cancellationToken);

    public Task<MatchCallUpDto> GetCallUpAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        getCallUp.Handle(new GetMatchCallUpQuery(squadId, matchId), cancellationToken);

    public Task<MatchCallUpDto> SaveCallUpAsync(Guid squadId, Guid matchId, CallUpRequest request, CancellationToken cancellationToken = default) =>
        saveCallUp.Handle(new SaveMatchCallUpCommand(squadId, matchId, request.CalledSquadPlayerIds, request.MeetAt, request.MeetPlace, request.Notes, request.Publish), cancellationToken);

    public Task<IReadOnlyList<MyTrainingDto>> MyTrainingsAsync(CancellationToken cancellationToken = default) =>
        myTrainings.Handle(new ListMyTrainingsQuery(), cancellationToken);

    public Task<TrainingSessionDto> ReportAbsenceAsync(Guid sessionId, AbsenceRequest request, CancellationToken cancellationToken = default) =>
        reportAbsence.Handle(new ReportTrainingAbsenceCommand(sessionId, request.WillMiss, request.Reason), cancellationToken);

    public Task<IReadOnlyList<MyCallUpDto>> MyCallUpsAsync(CancellationToken cancellationToken = default) =>
        myCallUps.Handle(new ListMyCallUpsQuery(), cancellationToken);

    public Task<MyCallUpDto> RespondCallUpAsync(Guid callUpId, CallUpResponseRequest request, CancellationToken cancellationToken = default) =>
        respondCallUp.Handle(new RespondCallUpCommand(callUpId, request.Response, request.Note), cancellationToken);
}
