using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// Lo que el cuerpo técnico quiere meter en una sesión: un ejercicio de la biblioteca (con su copia) o uno suelto, y quién
// lo hace (None = toda la plantilla).
public sealed record TrainingItemDraft(Guid? ExerciseId, string Name, ExerciseType Type, int DurationMinutes, string? Notes, TrainingGroup Groups = TrainingGroup.None);

// Quién hace un ejercicio, por líneas del campo. Sin ninguna (o con las cuatro) lo hace toda la plantilla.
[Flags]
public enum TrainingGroup
{
    None = 0,
    Goalkeepers = 1,
    Defenders = 2,
    Midfielders = 4,
    Forwards = 8,
    All = Goalkeepers | Defenders | Midfielders | Forwards,
}

public static class TrainingGroups
{
    private static readonly (Position Position, TrainingGroup Group)[] ByPosition =
    [
        (Position.Goalkeeper, TrainingGroup.Goalkeepers),
        (Position.Defender, TrainingGroup.Defenders),
        (Position.Midfielder, TrainingGroup.Midfielders),
        (Position.Forward, TrainingGroup.Forwards),
    ];

    public static TrainingGroup From(IEnumerable<Position>? positions) =>
        Normalize((positions ?? []).Aggregate(TrainingGroup.None, (groups, p) => groups | ByPosition.FirstOrDefault(x => x.Position == p).Group));

    public static TrainingGroup Normalize(TrainingGroup groups)
    {
        groups &= TrainingGroup.All;
        return groups == TrainingGroup.All ? TrainingGroup.None : groups;
    }

    // Vacío = toda la plantilla.
    public static IReadOnlyList<Position> Positions(TrainingGroup groups) =>
        ByPosition.Where(x => groups.HasFlag(x.Group) && x.Group != TrainingGroup.None).Select(x => x.Position).ToList();
}

// Una sesión de entrenamiento de una plantilla: cuándo, cuánto, de qué tipo, con qué ejercicios y quién viene. Cuando
// se crea con repetición semanal, cada sesión es una entidad propia unida a las demás por SeriesId.
public sealed class TrainingSession : AggregateRoot
{
    private readonly List<TrainingItem> _items = [];
    private readonly List<TrainingAttendance> _attendance = [];

    public Guid SquadId { get; private set; }
    public Guid? SeriesId { get; private set; }
    public TrainingKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public string? Location { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedByCoachId { get; private set; }
    public Guid? CreatedByClubId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<TrainingItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<TrainingAttendance> Attendance => _attendance.AsReadOnly();

    public DateTimeOffset EndsAt => StartsAt.AddMinutes(DurationMinutes);

    public bool HasStarted(DateTimeOffset now) => StartsAt <= now;

    public bool IsDone(DateTimeOffset now) => EndsAt <= now;

    private TrainingSession() { }

    public static TrainingSession Plan(
        Squad squad, SquadEditor editor, TrainingKind kind, string? title, DateTimeOffset startsAt, int durationMinutes,
        string? location, string? notes, DateTimeOffset now, Guid? seriesId = null)
    {
        var session = new TrainingSession
        {
            Id = NewId(),
            SquadId = squad.Id,
            SeriesId = seriesId,
            CreatedByCoachId = editor.CoachId,
            CreatedByClubId = editor.ClubId,
            CreatedAt = now,
        };
        session.Apply(kind, title, startsAt, durationMinutes, location, notes, now);
        // De entrada se cuenta con toda la plantilla; el cuerpo técnico y cada jugador van diciendo quién falta.
        foreach (var player in squad.ActivePlayers)
            session._attendance.Add(TrainingAttendance.Expected(session.Id, player.Id, now));
        return session;
    }

    // Las fechas de una serie semanal: desde el primer día, los días de la semana elegidos hasta la fecha tope.
    public static IReadOnlyList<DateTimeOffset> SeriesDates(DateTimeOffset first, IReadOnlyCollection<DayOfWeek> weekdays, DateOnly until, TimeZoneInfo zone)
    {
        if (weekdays.Count == 0)
            throw new DomainException(ErrorCode.TrainingRepeatWeekdays);

        var dates = new List<DateTimeOffset>();
        var local = TimeZoneInfo.ConvertTime(first, zone);
        var time = local.TimeOfDay;
        for (var day = DateOnly.FromDateTime(local.DateTime); day <= until; day = day.AddDays(1))
        {
            if (!weekdays.Contains(day.DayOfWeek)) continue;
            var localDate = day.ToDateTime(TimeOnly.FromTimeSpan(time), DateTimeKind.Unspecified);
            dates.Add(new DateTimeOffset(localDate, zone.GetUtcOffset(localDate)));
            if (dates.Count > TrainingCatalog.MaxSeriesSessions)
                throw new DomainException(ErrorCode.TrainingRepeatRange, TrainingCatalog.MaxSeriesSessions);
        }
        return dates;
    }

    // Un entrenamiento que ya ha acabado es historia: no cambia ni el día, ni la duración, ni los ejercicios. Lo único que
    // se sigue tocando es la lista (quién vino y quién faltó), y se puede anular si se creó por error.
    public void EnsureEditable(DateTimeOffset now)
    {
        if (IsDone(now))
            throw new DomainException(ErrorCode.TrainingPastLocked);
    }

    // Con ejercicios, la sesión dura lo que suman (el cuerpo técnico no la escribe aparte).
    public void Update(TrainingKind kind, string? title, DateTimeOffset startsAt, int durationMinutes, string? location, string? notes, DateTimeOffset now)
    {
        EnsureEditable(now);
        Apply(kind, title, startsAt, _items.Count > 0 ? PlannedMinutes : durationMinutes, location, notes, now);
    }

    private void Apply(TrainingKind kind, string? title, DateTimeOffset startsAt, int durationMinutes, string? location, string? notes, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
            throw new DomainException(ErrorCode.TrainingInvalidKind);
        if (durationMinutes is < TrainingCatalog.MinDurationMinutes or > TrainingCatalog.MaxDurationMinutes)
            throw new DomainException(ErrorCode.TrainingDurationRange, TrainingCatalog.MinDurationMinutes, TrainingCatalog.MaxDurationMinutes);

        Kind = kind;
        Title = string.IsNullOrWhiteSpace(title) ? TrainingCatalog.Info(kind).Label : Guard.NotEmpty(title, "título", 120);
        StartsAt = startsAt;
        DurationMinutes = durationMinutes;
        Location = string.IsNullOrWhiteSpace(location) ? null : Guard.NotEmpty(location, "lugar", 150);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.NotEmpty(notes, "notas", 2000);
        UpdatedAt = now;
    }

    // Los ejercicios de la sesión se guardan enteros, en el orden que llegan.
    public void ReplaceItems(IReadOnlyList<TrainingItemDraft> drafts, DateTimeOffset now)
    {
        EnsureEditable(now);
        if (drafts.Count > TrainingCatalog.MaxItems)
            throw new DomainException(ErrorCode.TrainingItemsTooMany, TrainingCatalog.MaxItems);

        var items = drafts.Select((draft, order) => TrainingItem.Create(Id, order, draft)).ToList();
        var planned = items.Sum(i => i.DurationMinutes);
        if (items.Count > 0 && planned is < TrainingCatalog.MinDurationMinutes or > TrainingCatalog.MaxDurationMinutes)
            throw new DomainException(ErrorCode.TrainingDurationRange, TrainingCatalog.MinDurationMinutes, TrainingCatalog.MaxDurationMinutes);

        _items.Clear();
        _items.AddRange(items);
        if (items.Count > 0) DurationMinutes = planned;
        UpdatedAt = now;
    }

    public int PlannedMinutes => _items.Sum(i => i.DurationMinutes);

    // Alta nueva en la plantilla después de preparar la sesión: se le cuenta como esperado, pero solo si ya era de la
    // plantilla cuando empezó (un fichaje de marzo no estaba citado a un entrenamiento de octubre).
    public void EnsureAttendanceFor(IEnumerable<SquadPlayer> players, DateTimeOffset now)
    {
        foreach (var player in players)
        {
            if (!player.IsActive || !WasInSquad(player) || _attendance.Any(a => a.SquadPlayerId == player.Id)) continue;
            _attendance.Add(TrainingAttendance.Expected(Id, player.Id, now));
        }
    }

    // Quien era de la plantilla a la hora de la sesión: dado de alta antes y sin baja hasta después.
    public bool WasInSquad(SquadPlayer player) => WasInSquadAt(player, StartsAt);

    public static bool WasInSquadAt(SquadPlayer player, DateTimeOffset at) =>
        player.AddedAt <= at && (player.LeftAt is null || player.LeftAt > at);

    // Lo que fue, una vez acabada la sesión: quien contaba con venir vino y quien avisó de que faltaba faltó. Antes de que
    // acabe, lo que hay anotado tal cual. El cuerpo técnico corrige después lo que no fuera así.
    public static AttendanceStatus Settled(AttendanceStatus status, bool done) =>
        !done ? status : status switch
        {
            AttendanceStatus.Expected => AttendanceStatus.Attended,
            AttendanceStatus.WillMiss => AttendanceStatus.Missed,
            _ => status,
        };

    public TrainingAttendance SetAttendance(Guid squadPlayerId, AttendanceStatus status, string? reason, DateTimeOffset now)
    {
        var entry = _attendance.FirstOrDefault(a => a.SquadPlayerId == squadPlayerId)
            ?? throw new DomainException(ErrorCode.SquadPlayerNotMember);
        return Set(entry, status, reason, now);
    }

    public TrainingAttendance SetTrialAttendance(Guid trialPlayerId, AttendanceStatus status, string? reason, DateTimeOffset now)
    {
        var entry = _attendance.FirstOrDefault(a => a.TrialPlayerId == trialPlayerId)
            ?? throw new DomainException(ErrorCode.TrialPlayerNotInSession);
        return Set(entry, status, reason, now);
    }

    private TrainingAttendance Set(TrainingAttendance entry, AttendanceStatus status, string? reason, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
            throw new DomainException(ErrorCode.TrainingAttendanceInvalid);
        if (IsDone(now))
        {
            if (status is AttendanceStatus.Expected or AttendanceStatus.WillMiss)
                throw new DomainException(ErrorCode.TrainingAttendanceSettled);
            // Confirmar lo que ya se daba por hecho no borra lo anotado: el «faltó» de quien avisó sigue diciendo que avisó.
            if (Settled(entry.Status, true) == status) status = entry.Status;
        }

        entry.Set(status, reason, now);
        UpdatedAt = now;
        return entry;
    }

    // El jugador avisa de que no va (o de que al final sí) antes de que empiece la sesión.
    public TrainingAttendance ReportOwnAbsence(Guid squadPlayerId, bool willMiss, string? reason, DateTimeOffset now)
    {
        if (HasStarted(now))
            throw new DomainException(ErrorCode.TrainingAbsencePast);

        return SetAttendance(squadPlayerId, willMiss ? AttendanceStatus.WillMiss : AttendanceStatus.Expected, reason, now);
    }

    public TrainingAttendance? AttendanceOf(Guid squadPlayerId) => _attendance.FirstOrDefault(a => a.SquadPlayerId == squadPlayerId);

    public TrainingAttendance? TrialAttendanceOf(Guid trialPlayerId) => _attendance.FirstOrDefault(a => a.TrialPlayerId == trialPlayerId);

    // Un jugador de prueba viene a esta sesión: se le pasa lista como a los demás (si ya estaba, no cambia nada).
    public TrainingAttendance AddTrialPlayer(TrialPlayer trialPlayer, DateTimeOffset now)
    {
        if (trialPlayer.SquadId != SquadId)
            throw new DomainException(ErrorCode.TrialPlayerNotFound, trialPlayer.Id);
        if (trialPlayer.IsPromoted)
            throw new DomainException(ErrorCode.TrialPlayerPromoted, trialPlayer.FullName);

        if (TrialAttendanceOf(trialPlayer.Id) is { } existing) return existing;
        var entry = TrainingAttendance.ForTrial(Id, trialPlayer.Id, now);
        _attendance.Add(entry);
        UpdatedAt = now;
        return entry;
    }

    public void RemoveTrialPlayer(Guid trialPlayerId, DateTimeOffset now)
    {
        var entry = TrialAttendanceOf(trialPlayerId)
            ?? throw new DomainException(ErrorCode.TrialPlayerNotInSession);
        _attendance.Remove(entry);
        UpdatedAt = now;
    }

    // Dos fichas de la misma persona que se juntan: su línea pasa a la que se queda. Si las dos tenían, se queda la de la
    // que se queda con lo anotado de la otra cuando la suya solo decía que se le esperaba.
    public void MergeAttendance(Guid fromSquadPlayerId, Guid toSquadPlayerId, DateTimeOffset now)
    {
        var from = AttendanceOf(fromSquadPlayerId);
        if (from is null) return;

        var to = AttendanceOf(toSquadPlayerId);
        if (to is null)
        {
            from.MoveToSquadPlayer(toSquadPlayerId);
        }
        else
        {
            if (to.Status == AttendanceStatus.Expected && from.Status != AttendanceStatus.Expected)
                to.Set(from.Status, from.Reason, now);
            _attendance.Remove(from);
        }

        UpdatedAt = now;
    }

    // Al jugador de prueba le han hecho ficha: su línea pasa a ser la del jugador de la plantilla, con lo que ya se
    // había anotado de él. Si por lo que sea ya tenía línea como jugador, se queda la de la ficha y se quita la de prueba.
    public void PromoteTrialPlayer(Guid trialPlayerId, Guid squadPlayerId, DateTimeOffset now)
    {
        var entry = TrialAttendanceOf(trialPlayerId);
        if (entry is null) return;

        if (AttendanceOf(squadPlayerId) is not null)
            _attendance.Remove(entry);
        else
            entry.MoveToSquadPlayer(squadPlayerId);
        UpdatedAt = now;
    }
}

public sealed class TrainingItem : Entity
{
    public Guid SessionId { get; private set; }
    public int Order { get; private set; }
    public Guid? ExerciseId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ExerciseType Type { get; private set; }
    public int DurationMinutes { get; private set; }
    public string? Notes { get; private set; }
    public TrainingGroup Groups { get; private set; }

    private TrainingItem() { }

    internal static TrainingItem Create(Guid sessionId, int order, TrainingItemDraft draft)
    {
        if (!Enum.IsDefined(draft.Type))
            throw new DomainException(ErrorCode.ExerciseInvalidType);
        if (draft.DurationMinutes is < 1 or > TrainingCatalog.MaxDurationMinutes)
            throw new DomainException(ErrorCode.ExerciseDurationRange, TrainingCatalog.MaxDurationMinutes);

        return new TrainingItem
        {
            Id = NewId(),
            SessionId = sessionId,
            Order = order,
            ExerciseId = draft.ExerciseId,
            Name = Guard.NotEmpty(draft.Name, "nombre", 120),
            Type = draft.Type,
            DurationMinutes = draft.DurationMinutes,
            Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : Guard.NotEmpty(draft.Notes, "notas", 500),
            Groups = TrainingGroups.Normalize(draft.Groups),
        };
    }
}

// Una línea de la lista: de un jugador de la plantilla (SquadPlayerId) o de uno que viene a prueba (TrialPlayerId),
// nunca de los dos.
public sealed class TrainingAttendance : Entity
{
    public Guid SessionId { get; private set; }
    public Guid? SquadPlayerId { get; private set; }
    public Guid? TrialPlayerId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTrial => TrialPlayerId is not null;

    private TrainingAttendance() { }

    internal static TrainingAttendance Expected(Guid sessionId, Guid squadPlayerId, DateTimeOffset now) => new()
    {
        Id = NewId(),
        SessionId = sessionId,
        SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId)),
        Status = AttendanceStatus.Expected,
        UpdatedAt = now,
    };

    internal static TrainingAttendance ForTrial(Guid sessionId, Guid trialPlayerId, DateTimeOffset now) => new()
    {
        Id = NewId(),
        SessionId = sessionId,
        TrialPlayerId = Guard.NotEmpty(trialPlayerId, nameof(trialPlayerId)),
        Status = AttendanceStatus.Expected,
        UpdatedAt = now,
    };

    internal void MoveToSquadPlayer(Guid squadPlayerId)
    {
        SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId));
        TrialPlayerId = null;
    }

    internal void Set(AttendanceStatus status, string? reason, DateTimeOffset now)
    {
        Status = status;
        Reason = status == AttendanceStatus.Expected || status == AttendanceStatus.Attended || string.IsNullOrWhiteSpace(reason)
            ? null
            : Guard.NotEmpty(reason, "motivo", 200);
        UpdatedAt = now;
    }
}

public interface ITrainingStore
{
    Task<TrainingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrainingSession>> GetBySquadAsync(Guid squadId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrainingSession>> GetUpcomingAsync(Guid squadId, DateTimeOffset from, int take, CancellationToken cancellationToken = default);

    // Las sesiones a las que vino (o se contaba con que viniera) un jugador de prueba.
    Task<IReadOnlyList<TrainingSession>> GetByTrialPlayerAsync(Guid trialPlayerId, CancellationToken cancellationToken = default);

    // Las sesiones con línea de ese jugador de la plantilla.
    Task<IReadOnlyList<TrainingSession>> GetBySquadPlayerAsync(Guid squadPlayerId, CancellationToken cancellationToken = default);

    // Por jugador de prueba de la plantilla: en cuántas sesiones está y a cuántas vino (en las ya acabadas, quien contaba
    // con venir cuenta como que vino).
    Task<IReadOnlyDictionary<Guid, TrialAttendanceCounts>> CountTrialAttendanceAsync(Guid squadId, DateTimeOffset now, CancellationToken cancellationToken = default);

    // Las sesiones de esas plantillas que empiezan en [from, to) y las líneas de sus jugadores de la plantilla (de todos o
    // solo de esos), sin cargar las sesiones enteras: para contar entrenamientos.
    Task<TrainingHistory> GetHistoryAsync(
        IReadOnlyCollection<Guid> squadIds, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? squadPlayerIds, CancellationToken cancellationToken = default);
    void Add(TrainingSession session);
    void Remove(TrainingSession session);
}

public sealed record TrialAttendanceCounts(int Sessions, int Attended);

public sealed record TrainingHistory(IReadOnlyList<TrainingSlot> Slots, IReadOnlyList<AttendanceMark> Marks)
{
    public IReadOnlyDictionary<(Guid SessionId, Guid SquadPlayerId), AttendanceStatus> MarksByLine() =>
        Marks.GroupBy(m => (m.SessionId, m.SquadPlayerId)).ToDictionary(g => g.Key, g => g.First().Status);
}
