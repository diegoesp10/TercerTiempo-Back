using System.ComponentModel;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record TrainingKindDto(TrainingKind Kind, string Label, string Description);

public sealed record ExerciseTypeDto(ExerciseType Type, string Label, string Description);

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
    string? Notes,
    [property: Description("Quién lo hace: posiciones; vacío = toda la plantilla.")]
    IReadOnlyList<TercerTiempo.Domain.Players.Position> Positions);

// La foto del jugador la pone el frontal con la ficha de la plantilla, que ya tiene cargada.
public sealed record TrainingAttendanceDto(
    [property: Description("Ficha de la plantilla; null si la línea es de un jugador de prueba.")]
    Guid? SquadPlayerId,
    [property: Description("Jugador de prueba (sin ficha); null si la línea es de un jugador de la plantilla.")]
    Guid? TrialPlayerId,
    string PlayerName,
    int? ShirtNumber,
    [property: Description("Tiene cuenta vinculada: puede avisar de que falta desde la app.")]
    bool Linked,
    [property: Description("Viene a prueba, sin ficha en la plantilla.")]
    bool Trial,
    [property: Description("Acabada la sesión, lo que fue: Attended (vino) o Missed (faltó); antes, Expected o WillMiss si aún no se ha pasado lista.")]
    AttendanceStatus Status,
    string? Reason,
    DateTimeOffset UpdatedAt,
    [property: Description("Faltó habiendo avisado antes (solo en sesiones acabadas).")]
    bool Warned = false,
    [property: Description("Estaba lesionado el día de la sesión: no entrena y no se le pasa lista (no cuenta como falta).")]
    bool Injured = false,
    [property: Description("La lesión que se lo impide; solo para el club, el cuerpo técnico y el propio jugador (los compañeros ven solo Injured).")]
    InjuryBriefDto? Injury = null,
    [property: Description("Causó baja en la plantilla después de esta sesión: su línea es historial.")]
    bool Left = false);

public sealed record AttendanceCountsDto(
    int Expected,
    int WillMiss,
    int Attended,
    int Missed,
    [property: Description("Lesionados ese día: no cuentan en lo demás.")]
    int Injured = 0);

public sealed record TrialPlayerDto(
    Guid Id,
    string FullName,
    string? Notes,
    DateTimeOffset CreatedAt,
    [property: Description("Entrenamientos en los que se le ha apuntado.")]
    int Sessions,
    [property: Description("Entrenamientos a los que vino.")]
    int Attended,
    [property: Description("Ficha de la plantilla que se le hizo; null mientras siga a prueba.")]
    Guid? PromotedSquadPlayerId,
    DateTimeOffset? PromotedAt);

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
    [property: Description("Ya ha acabado: no se puede cambiar; solo se pasa lista de quién vino y quién faltó.")]
    bool Finished,
    IReadOnlyList<TrainingItemDto> Items,
    [property: Description("Un jugador de la plantilla por línea (y los de prueba apuntados a esta sesión), con lo que se sabe de él.")]
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
    bool Finished,
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
    [property: Description("Pending (sin firmar), Confirmed (el convocado la ha firmado) o Acknowledged (el no convocado se ha dado por enterado).")]
    CallUpResponse Response,
    DateTimeOffset? RespondedAt,
    [property: Description("Cuándo le llegó el aviso de la convocatoria (un check); null si no se ha enviado o no tiene cuenta.")]
    DateTimeOffset? SentAt,
    [property: Description("Cuándo la abrió por primera vez (doble check); null si aún no la ha visto. Firmarla también cuenta como vista.")]
    DateTimeOffset? SeenAt,
    [property: Description("Está lesionado el día del partido: no puede ir convocado.")]
    bool Injured = false,
    [property: Description("La lesión; solo para el club, el cuerpo técnico y el propio jugador.")]
    InjuryBriefDto? Injury = null,
    [property: Description("Está sancionado para este partido (5.ª amarilla o expulsión en un partido anterior): no puede ir convocado.")]
    bool Suspended = false,
    SuspensionBriefDto? Suspension = null,
    [property: Description("Amarillas del ciclo que lleva al empezar el partido (de 0 a 4): con 4, la siguiente le cuesta un partido.")]
    int CycleYellows = 0,
    [property: Description("Causó baja en la plantilla después del partido: la línea es historial.")]
    bool Left = false)
{
    [Description("Apercibido para este partido: cuatro amarillas del ciclo y ninguna sanción; con una más se pierde el siguiente de liga. Misma regla que en la plantilla.")]
    public bool Warned => !Suspended && CycleYellows >= SuspensionCalendar.YellowsPerSuspension - 1;
}

public sealed record CallUpCountsDto(
    int Called,
    int Confirmed,
    int Pending,
    int Acknowledged,
    [property: Description("Jugadores con cuenta a los que les ha llegado la convocatoria.")]
    int Sent,
    [property: Description("Jugadores que la han abierto.")]
    int Seen);

public sealed record MatchCallUpDto(
    [property: Description("null si el cuerpo técnico aún no ha empezado la convocatoria de este partido.")]
    Guid? Id,
    Guid SquadId,
    MatchDto Match,
    CallUpStatus Status,
    DateTimeOffset? MeetAt,
    [property: Description("Campo de la liga donde se cita; null si no se ha elegido o se ha escrito otro sitio (meetPlace).")]
    TercerTiempo.Application.Grounds.GroundDto? MeetGround,
    [property: Description("Sitio escrito a mano cuando no es un campo de la liga (obras, campo alternativo…).")]
    string? MeetPlace,
    string? Notes,
    DateTimeOffset? PublishedAt,
    [property: Description("El partido ya ha empezado: la convocatoria se ve pero ya no se cambia ni se contesta.")]
    bool Closed,
    IReadOnlyList<CallUpEntryDto> Entries,
    CallUpCountsDto Counts,
    bool CanEdit,
    [property: Description("La línea de quien mira, si es un jugador de la plantilla.")]
    CallUpEntryDto? Mine,
    [property: Description("El campo del partido: el que trae el partido o, si no trae, el habitual del equipo local. Es el que se propone para la cita.")]
    TercerTiempo.Application.Grounds.GroundDto? MatchGround = null,
    [property: Description("A cuántos se convoca: el número fijo de la plantilla o el de esta convocatoria. null: aún no se ha dicho (o es de antes de que existiera).")]
    int? Size = null,
    [property: Description("La plantilla tiene un número fijo: manda ese y no se pregunta.")]
    bool SizeFixed = false,
    [property: Description("Sin número todavía: el de la última convocatoria, para proponerlo al preguntar. Solo para quien la prepara.")]
    int? SuggestedSize = null,
    [property: Description("Huella de lo que se ve (quién va, firmas, vistos, cita y si el partido ha empezado). GET …/callup/stamp da la de ahora: si no coincide, algo ha cambiado y hay que volver a pedir la convocatoria.")]
    string Stamp = "");

public sealed record MyCallUpDto(
    Guid CallUpId,
    Guid SquadId,
    string TeamName,
    MatchDto Match,
    DateTimeOffset? MeetAt,
    TercerTiempo.Application.Grounds.GroundDto? MeetGround,
    string? MeetPlace,
    string? Notes,
    DateTimeOffset PublishedAt,
    bool Closed,
    CallUpEntryDto Mine);

internal static class TrainingMapper
{
    public static TrainingCatalogDto Catalog() => new(
        TrainingCatalog.Kinds.Select(k => new TrainingKindDto(k.Kind, k.Label, k.Description)).ToList(),
        TrainingCatalog.ExerciseTypes.Select(t => new ExerciseTypeDto(t.Type, t.Label, t.Description)).ToList(),
        TrainingCatalog.MinDurationMinutes,
        TrainingCatalog.MaxDurationMinutes,
        TrainingCatalog.MaxItems,
        TrainingCatalog.MaxSeriesSessions);

    public static ExerciseDto ToDto(Exercise exercise) =>
        new(exercise.Id, exercise.Name, exercise.Type, TrainingCatalog.Info(exercise.Type).Label, exercise.DurationMinutes, exercise.Description, exercise.Material, exercise.UpdatedAt);

    public static IReadOnlyList<ExerciseDto> ToList(IEnumerable<Exercise> exercises) =>
        exercises.OrderBy(e => e.Type).ThenBy(e => e.Name).Select(ToDto).ToList();

    public static TrainingItemDto ToDto(TrainingItem item) =>
        new(item.Id, item.Order, item.ExerciseId, item.Name, item.Type, TrainingCatalog.Info(item.Type).Label, item.DurationMinutes, item.Notes, TrainingGroups.Positions(item.Groups));

    public static TrainingAttendanceDto ToDto(TrainingAttendance attendance, SquadPlayer? player, bool done) =>
        new(attendance.SquadPlayerId, null, player?.FullName ?? "Jugador", player?.ShirtNumber, player?.IsLinked ?? false, false,
            TrainingSession.Settled(attendance.Status, done), attendance.Reason, attendance.UpdatedAt, done && attendance.Status == AttendanceStatus.WillMiss,
            Left: player is { IsActive: false });

    public static TrainingAttendanceDto ToDto(TrainingAttendance attendance, TrialPlayer? trial, bool done) =>
        new(null, attendance.TrialPlayerId, trial?.FullName ?? "Jugador a prueba", null, false, true,
            TrainingSession.Settled(attendance.Status, done), attendance.Reason, attendance.UpdatedAt, done && attendance.Status == AttendanceStatus.WillMiss);

    public static TrialPlayerDto ToDto(TrialPlayer trial, TrialAttendanceCounts? counts) =>
        new(trial.Id, trial.FullName, trial.Notes, trial.CreatedAt, counts?.Sessions ?? 0, counts?.Attended ?? 0, trial.PromotedSquadPlayerId, trial.PromotedAt);

    // Las líneas que cuentan: las de quien era de la plantilla a la hora de la sesión (el que aún no tiene línea cuenta como
    // esperado; quien tenía línea y causó baja después sigue contando en esa) y las de prueba apuntadas a la sesión. Las
    // de quien ya se había ido (sesiones planificadas antes de su baja) no salen: ya no le tocan.
    private static IEnumerable<(TrainingAttendance? Line, SquadPlayer? Player, Guid? TrialPlayerId)> Rows(TrainingSession session, Squad squad)
    {
        foreach (var player in squad.Players)
        {
            var line = session.AttendanceOf(player.Id);
            if (player.IsActive ? line is not null || session.WasInSquad(player) : line is not null && session.WasInSquad(player))
                yield return (line, player, null);
        }
        foreach (var line in session.Attendance.Where(a => a.IsTrial))
            yield return (line, null, line.TrialPlayerId);
    }

    public static DateOnly DayOf(TrainingSession session) => CallUps.DayOf(session.StartsAt);

    // El que estaba lesionado ese día no entrena: su línea dice por qué y no cuenta como esperado, vino ni faltó.
    public static TrainingAttendanceDto WithInjury(TrainingAttendanceDto line, TrainingSession session, InjuryView injuries) =>
        line.SquadPlayerId is { } id && injuries.On(id, DayOf(session)) is { } injury
            ? line with { Injured = true, Injury = injuries.BriefFor(id, injury), Status = AttendanceStatus.Expected, Reason = null, Warned = false }
            : line;

    public static AttendanceCountsDto Counts(TrainingSession session, Squad squad, DateTimeOffset now, InjuryView? injuries = null)
    {
        var done = session.IsDone(now);
        var day = DayOf(session);
        var rows = Rows(session, squad)
            .Select(r => (Injured: r.Player is { } p && injuries?.On(p.Id, day) is not null, Status: TrainingSession.Settled(r.Line?.Status ?? AttendanceStatus.Expected, done)))
            .ToList();
        var statuses = rows.Where(r => !r.Injured).Select(r => r.Status).ToList();
        return new AttendanceCountsDto(
            statuses.Count(s => s == AttendanceStatus.Expected),
            statuses.Count(s => s == AttendanceStatus.WillMiss),
            statuses.Count(s => s == AttendanceStatus.Attended),
            statuses.Count(s => s == AttendanceStatus.Missed),
            rows.Count(r => r.Injured));
    }

    public static TrainingSessionDto ToDto(
        TrainingSession session, Squad squad, IReadOnlyDictionary<Guid, TrialPlayer> trials, bool canEdit, Guid? viewerSquadPlayerId, DateTimeOffset now,
        InjuryView? injuries = null)
    {
        injuries ??= InjuryView.None;
        var done = session.IsDone(now);
        var attendance = Rows(session, squad)
            .Select(r => r.Player is { } player
                ? WithInjury(r.Line is { } line
                    ? ToDto(line, player, done)
                    : new TrainingAttendanceDto(player.Id, null, player.FullName, player.ShirtNumber, player.IsLinked, false,
                        TrainingSession.Settled(AttendanceStatus.Expected, done), null, session.CreatedAt), session, injuries)
                : ToDto(r.Line!, trials.GetValueOrDefault(r.TrialPlayerId!.Value), done))
            // El motivo de una ausencia es de quien la avisa y del cuerpo técnico: los compañeros ven solo el estado.
            .Select(a => canEdit || a.SquadPlayerId == viewerSquadPlayerId ? a : a with { Reason = null })
            .OrderBy(a => a.Trial)
            .ThenBy(a => a.PlayerName)
            .ToList();
        var mine = viewerSquadPlayerId is { } me ? attendance.FirstOrDefault(a => a.SquadPlayerId == me) : null;

        return new TrainingSessionDto(
            session.Id, session.SquadId, session.SeriesId, session.Kind, TrainingCatalog.Info(session.Kind).Label, session.Title,
            session.StartsAt, session.EndsAt, session.DurationMinutes, session.Location, session.Notes, session.PlannedMinutes,
            session.HasStarted(now), done,
            session.Items.OrderBy(i => i.Order).Select(ToDto).ToList(),
            attendance, Counts(session, squad, now, injuries), canEdit, mine);
    }

    public static TrainingSummaryDto ToSummary(TrainingSession session, Squad squad, Guid? viewerSquadPlayerId, DateTimeOffset now, InjuryView? injuries = null)
    {
        injuries ??= InjuryView.None;
        var done = session.IsDone(now);
        var mine = viewerSquadPlayerId is { } me && session.AttendanceOf(me) is { } own
            ? WithInjury(ToDto(own, squad.Players.FirstOrDefault(p => p.Id == me), done), session, injuries)
            : null;
        return new TrainingSummaryDto(
            session.Id, session.SquadId, session.SeriesId, session.Kind, TrainingCatalog.Info(session.Kind).Label, session.Title,
            session.StartsAt, session.EndsAt, session.DurationMinutes, session.Location, session.Items.Count, session.PlannedMinutes,
            session.HasStarted(now), done, Counts(session, squad, now, injuries), mine);
    }

    public static CallUpEntryDto ToDto(CallUpEntry entry, SquadPlayer? player) =>
        new(entry.Id, entry.SquadPlayerId, player?.FullName ?? "Jugador", player?.ShirtNumber, player?.IsLinked ?? false, entry.Called, entry.Response, entry.RespondedAt,
            player?.IsLinked == true ? entry.NotifiedAt : null, entry.SeenAt, Left: player is { IsActive: false });

    // Las líneas de una convocatoria que cuentan: las de quien seguía en la plantilla a la hora del partido (quien causó
    // baja después sale como historial; quien se fue antes, ya no).
    public static IEnumerable<CallUpEntry> Members(IEnumerable<CallUpEntry> entries, IReadOnlyDictionary<Guid, SquadPlayer> players, DateTimeOffset? kickoffAt) =>
        entries.Where(e => players.TryGetValue(e.SquadPlayerId, out var p) && (p.IsActive || kickoffAt is { } at && p.WasMemberAt(at)));

    public static CallUpCountsDto Counts(IReadOnlyList<CallUpEntryDto> entries) =>
        new(entries.Count(e => e.Called),
            entries.Count(e => e.Called && e.Response == CallUpResponse.Confirmed),
            entries.Count(e => e.Called && e.Response == CallUpResponse.Pending),
            entries.Count(e => !e.Called && e.Response == CallUpResponse.Acknowledged),
            entries.Count(e => e.SentAt is not null),
            entries.Count(e => e.SeenAt is not null));

    public static CallUpEntryDto WithInjury(CallUpEntryDto entry, DateTimeOffset kickoffAt, InjuryView injuries) =>
        injuries.On(entry.SquadPlayerId, CallUps.DayOf(kickoffAt)) is { } injury
            ? entry with { Injured = true, Injury = injuries.BriefFor(entry.SquadPlayerId, injury) }
            : entry;

    // Los sancionados son públicos para la plantilla (los publica la federación); las amarillas que lleva, también.
    public static CallUpEntryDto WithSuspension(CallUpEntryDto entry, Guid matchId, SuspensionCalendar suspensions) =>
        suspensions.On(entry.SquadPlayerId, matchId) is { } suspension
            ? entry with { Suspended = true, Suspension = SuspensionReader.Brief(suspension) }
            : entry with { CycleYellows = suspensions.YellowsBefore(entry.SquadPlayerId, matchId) };

    public static MatchCallUpDto ToDto(
        MatchCallUp? callUp, Squad squad, MatchDto match, bool canEdit, Guid? viewerSquadPlayerId, DateTimeOffset now,
        TercerTiempo.Application.Grounds.GroundDto? meetGround = null, TercerTiempo.Application.Grounds.GroundDto? matchGround = null,
        InjuryView? injuries = null, SuspensionCalendar? suspensions = null, int? suggestedSize = null)
    {
        injuries ??= InjuryView.None;
        suspensions ??= SuspensionCalendar.Empty;
        var players = squad.Players.ToDictionary(p => p.Id);
        var entries = callUp is null
            ? squad.ActivePlayers.Select(p => new CallUpEntryDto(Guid.Empty, p.Id, p.FullName, p.ShirtNumber, p.IsLinked, false, CallUpResponse.Pending, null, null, null)).ToList()
            : Members(callUp.Entries, players, match.KickoffAt).Select(e => ToDto(e, players[e.SquadPlayerId])).ToList();
        entries = entries
            .Select(e => WithSuspension(WithInjury(e, match.KickoffAt, injuries), match.Id, suspensions))
            .OrderByDescending(e => e.Called).ThenBy(e => e.PlayerName).ToList();
        var mine = viewerSquadPlayerId is { } me ? entries.FirstOrDefault(e => e.SquadPlayerId == me) : null;

        return new MatchCallUpDto(
            callUp?.Id, squad.Id, match, callUp?.Status ?? CallUpStatus.Draft, callUp?.MeetAt, meetGround, callUp?.MeetPlace, callUp?.Notes,
            callUp?.PublishedAt, match.KickoffAt <= now, entries, Counts(entries), canEdit, mine, matchGround,
            squad.CallUpSize ?? callUp?.Size, squad.CallUpSize is not null, canEdit ? suggestedSize : null, CallUpStamp.Of(callUp, match.KickoffAt <= now));
    }
}

// Los entrenamientos se programan en la hora de los campos asturianos: la repetición semanal mantiene la misma hora
// local aunque cambie el horario de verano.
internal static class TrainingClock
{
    public static readonly TimeZoneInfo Zone = Resolve();

    // Tope de días por consulta del calendario y del listado de entrenamientos (el frontal pide como mucho seis semanas):
    // evita que un intervalo enorme cargue años de sesiones de golpe.
    public const int MaxRangeDays = 120;

    public static void EnsureRange(DateOnly from, DateOnly to)
    {
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            throw new DomainException(ErrorCode.CommonOutOfRange, "Días entre from y to", 1, MaxRangeDays);
    }

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
