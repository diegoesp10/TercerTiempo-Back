using System.ComponentModel;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record TrainingKindDto(TrainingKind Kind, string Label, string Description);

public sealed record ExerciseTypeDto(ExerciseType Type, string Label);

public sealed record TrainingCatalogDto(
    IReadOnlyList<TrainingKindDto> Kinds,
    IReadOnlyList<ExerciseTypeDto> ExerciseTypes,
    int MinDurationMinutes,
    int MaxDurationMinutes,
    int MaxItems,
    [property: Description("Sesiones como mucho al crear un entrenamiento con repetición semanal.")]
    int MaxSeriesSessions);

public sealed record ExerciseDto(
    Guid Id,
    string Name,
    ExerciseType Type,
    string TypeLabel,
    int DurationMinutes,
    string? Description,
    string? Material,
    DateTimeOffset UpdatedAt);

public sealed record TrainingItemDto(
    Guid Id,
    int Order,
    [property: Description("Ejercicio de la biblioteca del que se copió; null si se escribió suelto o se borró de la biblioteca.")]
    Guid? ExerciseId,
    string Name,
    ExerciseType Type,
    string TypeLabel,
    int DurationMinutes,
    string? Notes);

// La foto del jugador la pone el frontal con la ficha de la plantilla, que ya tiene cargada.
public sealed record TrainingAttendanceDto(
    Guid SquadPlayerId,
    string PlayerName,
    int? ShirtNumber,
    [property: Description("Tiene cuenta vinculada: puede avisar de que falta desde la app.")]
    bool Linked,
    AttendanceStatus Status,
    string? Reason,
    DateTimeOffset UpdatedAt);

public sealed record AttendanceCountsDto(int Expected, int WillMiss, int Attended, int Missed);

public sealed record TrainingSessionDto(
    Guid Id,
    Guid SquadId,
    [property: Description("Las sesiones creadas juntas con repetición semanal comparten SeriesId.")]
    Guid? SeriesId,
    TrainingKind Kind,
    string KindLabel,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int DurationMinutes,
    string? Location,
    string? Notes,
    [property: Description("Minutos que suman los ejercicios preparados.")]
    int PlannedMinutes,
    bool Started,
    IReadOnlyList<TrainingItemDto> Items,
    [property: Description("Un jugador de la plantilla por línea, con lo que se sabe de él en esta sesión.")]
    IReadOnlyList<TrainingAttendanceDto> Attendance,
    AttendanceCountsDto Counts,
    [property: Description("Quien mira puede editar la sesión (club o cuerpo técnico vigente).")]
    bool CanEdit,
    [property: Description("La línea de quien mira, si es un jugador de la plantilla.")]
    TrainingAttendanceDto? Mine);

public sealed record TrainingSummaryDto(
    Guid Id,
    Guid SquadId,
    Guid? SeriesId,
    TrainingKind Kind,
    string KindLabel,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int DurationMinutes,
    string? Location,
    int ItemsCount,
    int PlannedMinutes,
    bool Started,
    AttendanceCountsDto Counts,
    TrainingAttendanceDto? Mine);

public sealed record MyTrainingDto(
    TrainingSummaryDto Session,
    [property: Description("Equipo de la plantilla del jugador.")]
    string TeamName,
    Guid SquadId);

public sealed record CallUpEntryDto(
    Guid Id,
    Guid SquadPlayerId,
    string PlayerName,
    int? ShirtNumber,
    bool Linked,
    bool Called,
    CallUpResponse Response,
    string? ResponseNote,
    DateTimeOffset? RespondedAt);

public sealed record CallUpCountsDto(int Called, int Confirmed, int Declined, int Pending, int Acknowledged);

public sealed record MatchCallUpDto(
    [property: Description("null si el cuerpo técnico aún no ha empezado la convocatoria de este partido.")]
    Guid? Id,
    Guid SquadId,
    MatchDto Match,
    CallUpStatus Status,
    DateTimeOffset? MeetAt,
    string? MeetPlace,
    string? Notes,
    DateTimeOffset? PublishedAt,
    [property: Description("El partido ya ha empezado: la convocatoria se ve pero ya no se cambia ni se contesta.")]
    bool Closed,
    IReadOnlyList<CallUpEntryDto> Entries,
    CallUpCountsDto Counts,
    bool CanEdit,
    [property: Description("La línea de quien mira, si es un jugador de la plantilla.")]
    CallUpEntryDto? Mine);

public sealed record MyCallUpDto(
    Guid CallUpId,
    Guid SquadId,
    string TeamName,
    MatchDto Match,
    DateTimeOffset? MeetAt,
    string? MeetPlace,
    string? Notes,
    DateTimeOffset PublishedAt,
    bool Closed,
    CallUpEntryDto Mine);

internal static class TrainingMapper
{
    public static TrainingCatalogDto Catalog() => new(
        TrainingCatalog.Kinds.Select(k => new TrainingKindDto(k.Kind, k.Label, k.Description)).ToList(),
        TrainingCatalog.ExerciseTypes.Select(t => new ExerciseTypeDto(t.Type, t.Label)).ToList(),
        TrainingCatalog.MinDurationMinutes,
        TrainingCatalog.MaxDurationMinutes,
        TrainingCatalog.MaxItems,
        TrainingCatalog.MaxSeriesSessions);

    public static ExerciseDto ToDto(Exercise exercise) =>
        new(exercise.Id, exercise.Name, exercise.Type, TrainingCatalog.Info(exercise.Type).Label, exercise.DurationMinutes, exercise.Description, exercise.Material, exercise.UpdatedAt);

    public static IReadOnlyList<ExerciseDto> ToList(IEnumerable<Exercise> exercises) =>
        exercises.OrderBy(e => e.Type).ThenBy(e => e.Name).Select(ToDto).ToList();

    public static TrainingItemDto ToDto(TrainingItem item) =>
        new(item.Id, item.Order, item.ExerciseId, item.Name, item.Type, TrainingCatalog.Info(item.Type).Label, item.DurationMinutes, item.Notes);

    public static TrainingAttendanceDto ToDto(TrainingAttendance attendance, SquadPlayer? player) =>
        new(attendance.SquadPlayerId, player?.FullName ?? "Jugador", player?.ShirtNumber, player?.IsLinked ?? false, attendance.Status, attendance.Reason, attendance.UpdatedAt);

    public static AttendanceCountsDto Counts(TrainingSession session, Squad squad)
    {
        var active = squad.ActivePlayers.Select(p => p.Id).ToHashSet();
        var rows = session.Attendance.Where(a => active.Contains(a.SquadPlayerId)).ToList();
        return new AttendanceCountsDto(
            rows.Count(a => a.Status == AttendanceStatus.Expected),
            rows.Count(a => a.Status == AttendanceStatus.WillMiss),
            rows.Count(a => a.Status == AttendanceStatus.Attended),
            rows.Count(a => a.Status == AttendanceStatus.Missed));
    }

    public static TrainingSessionDto ToDto(TrainingSession session, Squad squad, bool canEdit, Guid? viewerSquadPlayerId, DateTimeOffset now)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        var active = squad.ActivePlayers.Select(p => p.Id).ToHashSet();
        var attendance = session.Attendance
            .Where(a => active.Contains(a.SquadPlayerId))
            .Select(a => ToDto(a, players.GetValueOrDefault(a.SquadPlayerId)))
            .OrderBy(a => a.ShirtNumber ?? 999)
            .ThenBy(a => a.PlayerName)
            .ToList();
        var mine = viewerSquadPlayerId is { } me ? attendance.FirstOrDefault(a => a.SquadPlayerId == me) : null;

        return new TrainingSessionDto(
            session.Id, session.SquadId, session.SeriesId, session.Kind, TrainingCatalog.Info(session.Kind).Label, session.Title,
            session.StartsAt, session.EndsAt, session.DurationMinutes, session.Location, session.Notes, session.PlannedMinutes,
            session.HasStarted(now),
            session.Items.OrderBy(i => i.Order).Select(ToDto).ToList(),
            attendance, Counts(session, squad), canEdit, mine);
    }

    public static TrainingSummaryDto ToSummary(TrainingSession session, Squad squad, Guid? viewerSquadPlayerId, DateTimeOffset now)
    {
        var mine = viewerSquadPlayerId is { } me && session.AttendanceOf(me) is { } own
            ? ToDto(own, squad.Players.FirstOrDefault(p => p.Id == me))
            : null;
        return new TrainingSummaryDto(
            session.Id, session.SquadId, session.SeriesId, session.Kind, TrainingCatalog.Info(session.Kind).Label, session.Title,
            session.StartsAt, session.EndsAt, session.DurationMinutes, session.Location, session.Items.Count, session.PlannedMinutes,
            session.HasStarted(now), Counts(session, squad), mine);
    }

    public static CallUpEntryDto ToDto(CallUpEntry entry, SquadPlayer? player) =>
        new(entry.Id, entry.SquadPlayerId, player?.FullName ?? "Jugador", player?.ShirtNumber, player?.IsLinked ?? false, entry.Called, entry.Response, entry.ResponseNote, entry.RespondedAt);

    public static CallUpCountsDto Counts(IReadOnlyList<CallUpEntryDto> entries) =>
        new(entries.Count(e => e.Called),
            entries.Count(e => e.Called && e.Response == CallUpResponse.Confirmed),
            entries.Count(e => e.Called && e.Response == CallUpResponse.Declined),
            entries.Count(e => e.Called && e.Response == CallUpResponse.Pending),
            entries.Count(e => !e.Called && e.Response == CallUpResponse.Acknowledged));

    public static MatchCallUpDto ToDto(MatchCallUp? callUp, Squad squad, MatchDto match, bool canEdit, Guid? viewerSquadPlayerId, DateTimeOffset now)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        var active = squad.ActivePlayers.Select(p => p.Id).ToHashSet();
        var entries = callUp is null
            ? squad.ActivePlayers.Select(p => new CallUpEntryDto(Guid.Empty, p.Id, p.FullName, p.ShirtNumber, p.IsLinked, false, CallUpResponse.Pending, null, null)).ToList()
            : callUp.Entries.Where(e => active.Contains(e.SquadPlayerId)).Select(e => ToDto(e, players.GetValueOrDefault(e.SquadPlayerId))).ToList();
        entries = entries.OrderByDescending(e => e.Called).ThenBy(e => e.ShirtNumber ?? 999).ThenBy(e => e.PlayerName).ToList();
        var mine = viewerSquadPlayerId is { } me ? entries.FirstOrDefault(e => e.SquadPlayerId == me) : null;

        return new MatchCallUpDto(
            callUp?.Id, squad.Id, match, callUp?.Status ?? CallUpStatus.Draft, callUp?.MeetAt, callUp?.MeetPlace, callUp?.Notes,
            callUp?.PublishedAt, match.KickoffAt <= now, entries, Counts(entries), canEdit, mine);
    }
}

// Los entrenamientos se programan en la hora de los campos asturianos: la repetición semanal mantiene la misma hora
// local aunque cambie el horario de verano.
internal static class TrainingClock
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Madrid", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Se prueba el siguiente nombre.
            }
            catch (InvalidTimeZoneException)
            {
                // Se prueba el siguiente nombre.
            }
        }
        return TimeZoneInfo.Utc;
    }
}
