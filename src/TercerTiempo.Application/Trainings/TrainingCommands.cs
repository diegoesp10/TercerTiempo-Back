using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record GetTrainingCatalogQuery : IQuery<TrainingCatalogDto>;

public sealed record ListExercisesQuery(Guid SquadId) : IQuery<IReadOnlyList<ExerciseDto>>;

public sealed record CreateExerciseCommand(Guid SquadId, string Name, ExerciseType Type, int DurationMinutes, string? Description, string? Material) : ICommand<IReadOnlyList<ExerciseDto>>;

public sealed record UpdateExerciseCommand(Guid SquadId, Guid ExerciseId, string Name, ExerciseType Type, int DurationMinutes, string? Description, string? Material) : ICommand<IReadOnlyList<ExerciseDto>>;

public sealed record DeleteExerciseCommand(Guid SquadId, Guid ExerciseId) : ICommand<IReadOnlyList<ExerciseDto>>;

public sealed record TrainingItemInput(
    [property: Description("Ejercicio de la biblioteca del que se copia; null si se escribe suelto.")]
    Guid? ExerciseId,
    string Name,
    ExerciseType Type,
    int DurationMinutes,
    string? Notes,
    [property: Description("Quién lo hace: posiciones (Goalkeeper, Defender, Midfielder, Forward); vacío o null = toda la plantilla.")]
    IReadOnlyList<Position>? Positions = null);

public sealed record ListTrainingsQuery(Guid SquadId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<TrainingSummaryDto>>;

public sealed record GetTrainingQuery(Guid SquadId, Guid SessionId) : IQuery<TrainingSessionDto>;

public sealed record PlanTrainingCommand(
    Guid SquadId,
    TrainingKind Kind,
    string? Title,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string? Location,
    string? Notes,
    IReadOnlyList<TrainingItemInput> Items,
    [property: Description("Días de la semana en los que se repite (Monday…Sunday); vacío = una sola sesión.")]
    IReadOnlyList<DayOfWeek> RepeatWeekdays,
    [property: Description("Hasta qué día (incluido) se repite; obligatorio con días de repetición.")]
    DateOnly? RepeatUntil) : ICommand<IReadOnlyList<TrainingSummaryDto>>;

public sealed record UpdateTrainingCommand(
    Guid SquadId, Guid SessionId, TrainingKind Kind, string? Title, DateTimeOffset StartsAt, int DurationMinutes, string? Location, string? Notes)
    : ICommand<TrainingSessionDto>;

public sealed record SetTrainingItemsCommand(Guid SquadId, Guid SessionId, IReadOnlyList<TrainingItemInput> Items) : ICommand<TrainingSessionDto>;

// Cada línea es de un jugador de la plantilla o de uno de prueba apuntado a la sesión.
public sealed record AttendanceInput(Guid? SquadPlayerId, Guid? TrialPlayerId, AttendanceStatus Status, string? Reason)
{
    public bool HasOneTarget => (SquadPlayerId is { } s && s != Guid.Empty) ^ (TrialPlayerId is { } t && t != Guid.Empty);
}

public sealed record SetAttendanceCommand(Guid SquadId, Guid SessionId, IReadOnlyList<AttendanceInput> Entries) : ICommand<TrainingSessionDto>;

public sealed record DeleteTrainingCommand(Guid SquadId, Guid SessionId) : ICommand<bool>;

// El jugador avisa de que no va a un entrenamiento (o de que al final sí).
public sealed record ReportTrainingAbsenceCommand(Guid SessionId, bool WillMiss, string? Reason) : ICommand<TrainingSessionDto>;

public sealed record ListMyTrainingsQuery : IQuery<IReadOnlyList<MyTrainingDto>>;

internal sealed class CreateExerciseValidator : AbstractValidator<CreateExerciseCommand>
{
    public CreateExerciseValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, TrainingCatalog.MaxDurationMinutes).WithCatalogMessage(ErrorCode.ExerciseDurationRange, TrainingCatalog.MaxDurationMinutes);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Material).MaximumLength(300);
    }
}

internal sealed class UpdateExerciseValidator : AbstractValidator<UpdateExerciseCommand>
{
    public UpdateExerciseValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.ExerciseId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, TrainingCatalog.MaxDurationMinutes).WithCatalogMessage(ErrorCode.ExerciseDurationRange, TrainingCatalog.MaxDurationMinutes);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Material).MaximumLength(300);
    }
}

internal sealed class TrainingItemInputValidator : AbstractValidator<TrainingItemInput>
{
    public TrainingItemInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, TrainingCatalog.MaxDurationMinutes).WithCatalogMessage(ErrorCode.ExerciseDurationRange, TrainingCatalog.MaxDurationMinutes);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleForEach(x => x.Positions).IsInEnum();
    }
}

internal sealed class PlanTrainingValidator : AbstractValidator<PlanTrainingCommand>
{
    public PlanTrainingValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Title).MaximumLength(120);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(TrainingCatalog.MinDurationMinutes, TrainingCatalog.MaxDurationMinutes)
            .WithCatalogMessage(ErrorCode.TrainingDurationRange, TrainingCatalog.MinDurationMinutes, TrainingCatalog.MaxDurationMinutes);
        RuleFor(x => x.Location).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Items).NotNull();
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(TrainingCatalog.MaxItems).WithCatalogMessage(ErrorCode.TrainingItemsTooMany, TrainingCatalog.MaxItems);
        RuleForEach(x => x.Items).SetValidator(new TrainingItemInputValidator());
        RuleFor(x => x.RepeatWeekdays).NotNull();
        RuleForEach(x => x.RepeatWeekdays).IsInEnum();
        RuleFor(x => x.RepeatUntil).NotNull().When(x => x.RepeatWeekdays.Count > 0).WithCatalogMessage(ErrorCode.TrainingRepeatWeekdays);
    }
}

internal sealed class UpdateTrainingValidator : AbstractValidator<UpdateTrainingCommand>
{
    public UpdateTrainingValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Title).MaximumLength(120);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(TrainingCatalog.MinDurationMinutes, TrainingCatalog.MaxDurationMinutes)
            .WithCatalogMessage(ErrorCode.TrainingDurationRange, TrainingCatalog.MinDurationMinutes, TrainingCatalog.MaxDurationMinutes);
        RuleFor(x => x.Location).MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class SetTrainingItemsValidator : AbstractValidator<SetTrainingItemsCommand>
{
    public SetTrainingItemsValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Items).NotNull();
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(TrainingCatalog.MaxItems).WithCatalogMessage(ErrorCode.TrainingItemsTooMany, TrainingCatalog.MaxItems);
        RuleForEach(x => x.Items).SetValidator(new TrainingItemInputValidator());
    }
}

internal sealed class SetAttendanceValidator : AbstractValidator<SetAttendanceCommand>
{
    public SetAttendanceValidator()
    {
        RuleFor(x => x.SquadId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Entries).NotNull();
        // Una línea por jugador o jugador a prueba: una plantilla no pasa de unas decenas; miles de líneas solo cargarían el servidor.
        RuleFor(x => x.Entries.Count).LessThanOrEqualTo(TrainingCatalog.MaxAttendanceEntries).When(x => x.Entries is not null)
            .WithCatalogMessage(ErrorCode.CommonOutOfRange, "Asistencia", 1, TrainingCatalog.MaxAttendanceEntries);
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.HasOneTarget).Equal(true).WithCatalogMessage(ErrorCode.TrainingAttendanceTarget);
            entry.RuleFor(e => e.Status).IsInEnum();
            entry.RuleFor(e => e.Reason).MaximumLength(200);
        });
    }
}

internal sealed class ReportTrainingAbsenceValidator : AbstractValidator<ReportTrainingAbsenceCommand>
{
    public ReportTrainingAbsenceValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(200);
    }
}

// Quién ve y quién toca los entrenamientos: lo ven el club, el cuerpo técnico, los jugadores vinculados y el
// administrador; lo preparan el club y el cuerpo técnico vigente.
internal static class TrainingAccess
{
    public static async Task<Squad> SquadForViewerAsync(ISquadStore squads, IClubStore clubs, ICurrentUser currentUser, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthenticatedException();

        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);

        if (!await SquadAccess.IsInsiderAsync(squad, currentUser, clock, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.SquadPrivate);

        return squad;
    }

    public static async Task<TrainingSession> SessionOfAsync(Squad squad, Guid sessionId, ITrainingStore trainings, CancellationToken cancellationToken)
    {
        var session = await trainings.GetByIdAsync(sessionId, cancellationToken);
        if (session is null || session.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.TrainingNotFound, sessionId);
        return session;
    }

    public static Guid? ViewerEntry(Squad squad, ICurrentUser user) =>
        user.PlayerId is { } playerId ? squad.PlayerEntryOf(playerId)?.Id : null;

    public static Task<bool> CanEditAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        SquadAccess.IsManagerAsync(squad, user, clock, clubs, cancellationToken);

    // El enlace a la biblioteca solo vale para ejercicios de esta plantilla: un id de otra se descarta y el ejercicio se
    // queda como copia suelta (con su nombre, tipo y duración), igual que cuando se borra el de la biblioteca.
    public static async Task<IReadOnlyList<TrainingItemDraft>> DraftsAsync(
        IReadOnlyList<TrainingItemInput> items, Guid squadId, IExerciseStore exercises, CancellationToken cancellationToken)
    {
        var own = items.Any(i => i.ExerciseId is not null)
            ? (await exercises.GetBySquadAsync(squadId, cancellationToken)).Select(e => e.Id).ToHashSet()
            : [];
        return items
            .Select(i => new TrainingItemDraft(i.ExerciseId is { } id && own.Contains(id) ? id : null, i.Name, i.Type, i.DurationMinutes, i.Notes, TrainingGroups.From(i.Positions)))
            .ToList();
    }

    public static async Task<TrainingSessionDto> DetailAsync(
        TrainingSession session, Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, ITrialPlayerStore trials, IInjuryStore injuries,
        CancellationToken cancellationToken)
    {
        var trialMap = session.Attendance.Any(a => a.IsTrial)
            ? (await trials.GetBySquadAsync(squad.Id, cancellationToken)).ToDictionary(t => t.Id)
            : new Dictionary<Guid, TrialPlayer>();
        var injuryView = await InjuryView.LoadAsync(squad, user, clock, clubs, injuries, cancellationToken);
        return TrainingMapper.ToDto(session, squad, trialMap, await CanEditAsync(squad, user, clock, clubs, cancellationToken), ViewerEntry(squad, user), clock.GetUtcNow(), injuryView);
    }

    // Un lesionado ese día no entrena: ni se le pasa lista ni puede avisar de que falta.
    public static async Task EnsureNotInjuredAsync(Squad squad, TrainingSession session, IEnumerable<Guid> squadPlayerIds, IInjuryStore injuries, CancellationToken cancellationToken)
    {
        var ids = squadPlayerIds.ToList();
        if (ids.Count == 0) return;
        var calendar = new InjuryCalendar(await injuries.GetBySquadAsync(squad.Id, cancellationToken));
        var day = TrainingMapper.DayOf(session);
        foreach (var id in ids)
            if (calendar.On(id, day, session.Id) is not null)
                throw new DomainException(ErrorCode.TrainingPlayerInjured, squad.Players.FirstOrDefault(p => p.Id == id)?.FullName ?? "El jugador");
    }
}

// Avisos al cuerpo técnico vigente y al club del equipo (si lo tiene): cada uno con su copia.
internal static class StaffAlerts
{
    public static async Task RaiseAsync(
        Squad squad, IClubStore clubs, IAlertStore alerts, AlertKind kind, string key, string title, string message, string? link, DateOnly? dueOn,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var member in squad.StaffOn(SquadAccess.Today(clock)))
            alerts.Add(Alert.ForCoach(member.CoachId, kind, key, title, message, link, dueOn, now));
        if (await clubs.GetByTeamAsync(squad.TeamId, cancellationToken) is { } club)
            alerts.Add(Alert.ForClub(club.Id, kind, key, title, message, link, dueOn, now));
    }

    // Los abiertos del cuerpo técnico y del club, de una vez.
    public static async Task<IReadOnlyList<Alert>> OpenAsync(Squad squad, IClubStore clubs, IAlertStore alerts, string keyPrefix, TimeProvider clock, CancellationToken cancellationToken)
    {
        var club = await clubs.GetByTeamAsync(squad.TeamId, cancellationToken);
        var coaches = squad.StaffOn(SquadAccess.Today(clock)).Select(m => m.CoachId).Distinct().ToList();
        return await alerts.GetOpenByKeyPrefixAsync(keyPrefix, [], coaches, club?.Id, cancellationToken);
    }

    public static async Task ResolveAsync(Squad squad, IClubStore clubs, IAlertStore alerts, string keyPrefix, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var alert in await OpenAsync(squad, clubs, alerts, keyPrefix, clock, cancellationToken))
            alert.Resolve(now);
    }
}

internal sealed class GetTrainingCatalogHandler : IQueryHandler<GetTrainingCatalogQuery, TrainingCatalogDto>
{
    public Task<TrainingCatalogDto> Handle(GetTrainingCatalogQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(TrainingMapper.Catalog());
}

internal sealed class ListExercisesHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IExerciseStore exercises, TimeProvider clock)
    : IQueryHandler<ListExercisesQuery, IReadOnlyList<ExerciseDto>>
{
    public async Task<IReadOnlyList<ExerciseDto>> Handle(ListExercisesQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        return TrainingMapper.ToList(await exercises.GetBySquadAsync(squad.Id, cancellationToken));
    }
}

internal sealed class CreateExerciseHandler(ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IExerciseStore exercises, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateExerciseCommand, IReadOnlyList<ExerciseDto>>
{
    public async Task<IReadOnlyList<ExerciseDto>> Handle(CreateExerciseCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        exercises.Add(Exercise.Create(squad, editor, command.Name, command.Type, command.DurationMinutes, command.Description, command.Material, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TrainingMapper.ToList(await exercises.GetBySquadAsync(squad.Id, cancellationToken));
    }
}

internal sealed class UpdateExerciseHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IExerciseStore exercises, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateExerciseCommand, IReadOnlyList<ExerciseDto>>
{
    public async Task<IReadOnlyList<ExerciseDto>> Handle(UpdateExerciseCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var exercise = await exercises.GetByIdAsync(command.ExerciseId, cancellationToken);
        if (exercise is null || exercise.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.ExerciseNotFound, command.ExerciseId);

        exercise.Update(command.Name, command.Type, command.DurationMinutes, command.Description, command.Material, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TrainingMapper.ToList(await exercises.GetBySquadAsync(squad.Id, cancellationToken));
    }
}

internal sealed class DeleteExerciseHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IExerciseStore exercises, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DeleteExerciseCommand, IReadOnlyList<ExerciseDto>>
{
    public async Task<IReadOnlyList<ExerciseDto>> Handle(DeleteExerciseCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var exercise = await exercises.GetByIdAsync(command.ExerciseId, cancellationToken);
        if (exercise is null || exercise.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.ExerciseNotFound, command.ExerciseId);

        await exercises.RemoveAsync(exercise, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TrainingMapper.ToList(await exercises.GetBySquadAsync(squad.Id, cancellationToken));
    }
}

internal sealed class ListTrainingsHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, IInjuryStore injuries, TimeProvider clock)
    : IQueryHandler<ListTrainingsQuery, IReadOnlyList<TrainingSummaryDto>>
{
    public async Task<IReadOnlyList<TrainingSummaryDto>> Handle(ListTrainingsQuery query, CancellationToken cancellationToken)
    {
        TrainingClock.EnsureRange(query.From, query.To);
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var zone = TrainingClock.Zone;
        var from = new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(query.From.ToDateTime(TimeOnly.MinValue)));
        var toDate = query.To.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var to = new DateTimeOffset(toDate, zone.GetUtcOffset(toDate));
        var viewer = TrainingAccess.ViewerEntry(squad, currentUser);
        var now = clock.GetUtcNow();
        var injuryView = await InjuryView.LoadAsync(squad, currentUser, clock, clubs, injuries, cancellationToken);
        return (await trainings.GetBySquadAsync(squad.Id, from, to, cancellationToken))
            .Select(s => TrainingMapper.ToSummary(s, squad, viewer, now, injuryView))
            .ToList();
    }
}

internal sealed class GetTrainingHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, ITrialPlayerStore trials, IInjuryStore injuries, TimeProvider clock)
    : IQueryHandler<GetTrainingQuery, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(GetTrainingQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, query.SessionId, trainings, cancellationToken);
        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, injuries, cancellationToken);
    }
}

internal sealed class PlanTrainingHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, ITrainingStore trainings, IExerciseStore exercises, IAlertStore alerts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<PlanTrainingCommand, IReadOnlyList<TrainingSummaryDto>>
{
    public async Task<IReadOnlyList<TrainingSummaryDto>> Handle(PlanTrainingCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        var now = clock.GetUtcNow();
        var drafts = await TrainingAccess.DraftsAsync(command.Items, squad.Id, exercises, cancellationToken);

        var dates = command.RepeatWeekdays.Count > 0
            ? TrainingSession.SeriesDates(command.StartsAt, command.RepeatWeekdays.ToHashSet(), command.RepeatUntil ?? throw new DomainException(ErrorCode.TrainingRepeatWeekdays), TrainingClock.Zone)
            : [command.StartsAt];
        if (dates.Count == 0)
            throw new DomainException(ErrorCode.TrainingRepeatWeekdays);

        var seriesId = dates.Count > 1 ? Guid.CreateVersion7() : (Guid?)null;
        var created = new List<TrainingSession>();
        foreach (var date in dates)
        {
            var session = TrainingSession.Plan(squad, editor, command.Kind, command.Title, date, command.DurationMinutes, command.Location, command.Notes, now, seriesId);
            session.ReplaceItems(drafts, now);
            trainings.Add(session);
            created.Add(session);
        }
        await TrainingCalls.PlannedAsync(squad, created, alerts, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return created.Select(s => TrainingMapper.ToSummary(s, squad, null, now)).ToList();
    }
}

internal sealed class UpdateTrainingHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, ITrialPlayerStore trials, IAlertStore alerts, IUnitOfWork unitOfWork,
    IInjuryStore injuries, TimeProvider clock)
    : ICommandHandler<UpdateTrainingCommand, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(UpdateTrainingCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, command.SessionId, trainings, cancellationToken);
        var now = clock.GetUtcNow();
        var (previousStart, previousLocation) = (session.StartsAt, session.Location);
        session.Update(command.Kind, command.Title, command.StartsAt, command.DurationMinutes, command.Location, command.Notes, now);
        // Si alguien se lesionó en ella (está en curso), su lesión copia el título y la hora de ahora.
        foreach (var injury in await injuries.GetByTrainingSessionAsync(session.Id, cancellationToken))
            injury.RefreshOccasion(InjuryOccasions.Of(session), now);
        // Solo se avisa si cambia lo que el jugador necesita para llegar (día, hora o sitio) y aún no ha pasado.
        if ((session.StartsAt != previousStart || session.Location != previousLocation) && !session.HasStarted(now) && previousStart > now)
            await TrainingCalls.MovedAsync(squad, session, previousStart, alerts, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, injuries, cancellationToken);
    }
}

internal sealed class SetTrainingItemsHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, IExerciseStore exercises, ITrialPlayerStore trials, IUnitOfWork unitOfWork, IInjuryStore injuries, TimeProvider clock)
    : ICommandHandler<SetTrainingItemsCommand, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(SetTrainingItemsCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, command.SessionId, trainings, cancellationToken);
        session.ReplaceItems(await TrainingAccess.DraftsAsync(command.Items, squad.Id, exercises, cancellationToken), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, injuries, cancellationToken);
    }
}

internal sealed class SetAttendanceHandler(ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, ITrialPlayerStore trials, IUnitOfWork unitOfWork, IInjuryStore injuries, TimeProvider clock)
    : ICommandHandler<SetAttendanceCommand, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(SetAttendanceCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, command.SessionId, trainings, cancellationToken);
        var now = clock.GetUtcNow();
        session.EnsureAttendanceFor(squad.ActivePlayers, now);
        await TrainingAccess.EnsureNotInjuredAsync(squad, session, command.Entries.Where(e => e.SquadPlayerId is not null).Select(e => e.SquadPlayerId!.Value), injuries, cancellationToken);
        foreach (var entry in command.Entries)
        {
            if (entry.SquadPlayerId is { } squadPlayerId)
            {
                var player = squad.Players.FirstOrDefault(p => p.Id == squadPlayerId)
                    ?? throw new NotFoundException(ErrorCode.SquadPlayerNotFound, squadPlayerId);
                // De quien causó baja solo se corrige la lista de cuando era de la plantilla; una línea suya de después
                // (de una pantalla abierta antes de la baja) no cuenta y se ignora.
                if (!player.IsActive && !session.WasInSquad(player)) continue;
                session.SetAttendance(squadPlayerId, entry.Status, entry.Reason, now);
            }
            else if (entry.TrialPlayerId is { } trialPlayerId)
            {
                session.SetTrialAttendance(trialPlayerId, entry.Status, entry.Reason, now);
            }
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, injuries, cancellationToken);
    }
}

internal sealed class DeleteTrainingHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, IAlertStore alerts, IInjuryStore injuries, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<DeleteTrainingCommand, bool>
{
    public async Task<bool> Handle(DeleteTrainingCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var session = await TrainingAccess.SessionOfAsync(squad, command.SessionId, trainings, cancellationToken);

        // Se borra de una en una y solo lo que no ha acabado: lo ya entrenado es historia (y cuenta en la asistencia).
        var now = clock.GetUtcNow();
        session.EnsureEditable(now);
        // Si alguien se lesionó en ella (estaba en curso), la lesión se queda, ya sin saber dónde fue.
        foreach (var injury in await injuries.GetByTrainingSessionAsync(session.Id, cancellationToken))
            injury.ForgetOccasion(now);
        trainings.Remove(session);

        // Anular una sesión en curso no le cambia nada al jugador: solo se avisa si aún no había empezado.
        if (!session.HasStarted(now))
            await TrainingCalls.CancelledAsync(squad, session, alerts, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed class ReportTrainingAbsenceHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, ITrialPlayerStore trials, ITeamStore teams,
    IAlertStore alerts, IUnitOfWork unitOfWork, IInjuryStore injuries, TimeProvider clock)
    : ICommandHandler<ReportTrainingAbsenceCommand, TrainingSessionDto>
{
    public async Task<TrainingSessionDto> Handle(ReportTrainingAbsenceCommand command, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var session = await trainings.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TrainingNotFound, command.SessionId);
        var squad = await squads.GetByIdAsync(session.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, session.SquadId);
        var entry = squad.PlayerEntryOf(player.Id)
            ?? throw new ForbiddenException(ErrorCode.SquadPrivate);

        var now = clock.GetUtcNow();
        session.EnsureAttendanceFor(squad.ActivePlayers, now);
        await TrainingAccess.EnsureNotInjuredAsync(squad, session, [entry.Id], injuries, cancellationToken);
        var attendance = session.ReportOwnAbsence(entry.Id, command.WillMiss, command.Reason, now);

        var keyPrefix = $"training-absence:{attendance.Id}";
        await StaffAlerts.ResolveAsync(squad, clubs, alerts, keyPrefix, clock, cancellationToken);
        if (command.WillMiss)
        {
            var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
            var when = TimeZoneInfo.ConvertTime(session.StartsAt, TrainingClock.Zone).ToString("dd/MM HH:mm");
            var reason = string.IsNullOrWhiteSpace(attendance.Reason) ? "" : $" Motivo: {attendance.Reason}.";
            await StaffAlerts.RaiseAsync(squad, clubs, alerts, AlertKind.TrainingAbsence, $"{keyPrefix}:{now.UtcTicks}",
                $"{entry.FullName} falta al entrenamiento",
                $"{entry.FullName} avisa de que no va al entrenamiento del {when} ({session.Title}) de {team?.Name ?? "su equipo"}.{reason}",
                $"/plantillas/{squad.Id}/agenda?sesion={session.Id}", DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(session.StartsAt, TrainingClock.Zone).DateTime), clock, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await TrainingAccess.DetailAsync(session, squad, currentUser, clock, clubs, trials, injuries, cancellationToken);
    }
}

internal sealed class ListMyTrainingsHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, ITeamStore teams, ITrainingStore trainings, IClubStore clubs, IInjuryStore injuries, TimeProvider clock)
    : IQueryHandler<ListMyTrainingsQuery, IReadOnlyList<MyTrainingDto>>
{
    private const int Shown = 10;

    public async Task<IReadOnlyList<MyTrainingDto>> Handle(ListMyTrainingsQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad is null) return [];

        var now = clock.GetUtcNow();
        var viewer = squad.PlayerEntryOf(player.Id)?.Id;
        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken);
        // Desde el principio del día: el entrenamiento de hoy sigue en la lista aunque ya haya empezado.
        var from = TimeZoneInfo.ConvertTime(now, TrainingClock.Zone).Date;
        var fromAt = new DateTimeOffset(from, TrainingClock.Zone.GetUtcOffset(from));
        var injuryView = await InjuryView.LoadAsync(squad, currentUser, clock, clubs, injuries, cancellationToken);
        return (await trainings.GetUpcomingAsync(squad.Id, fromAt, Shown, cancellationToken))
            .Select(s => new MyTrainingDto(TrainingMapper.ToSummary(s, squad, viewer, now, injuryView), team?.Name ?? "Tu equipo", squad.Id))
            .ToList();
    }
}
