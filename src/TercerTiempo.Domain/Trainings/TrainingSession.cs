using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// Lo que el cuerpo técnico quiere meter en una sesión: un ejercicio de la biblioteca (con su copia) o uno suelto.
public sealed record TrainingItemDraft(Guid? ExerciseId, string Name, ExerciseType Type, int DurationMinutes, string? Notes);

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
        session.Update(kind, title, startsAt, durationMinutes, location, notes, now);
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

    public void Update(TrainingKind kind, string? title, DateTimeOffset startsAt, int durationMinutes, string? location, string? notes, DateTimeOffset now)
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
        if (drafts.Count > TrainingCatalog.MaxItems)
            throw new DomainException(ErrorCode.TrainingItemsTooMany, TrainingCatalog.MaxItems);

        _items.Clear();
        var order = 0;
        foreach (var draft in drafts)
            _items.Add(TrainingItem.Create(Id, order++, draft));
        UpdatedAt = now;
    }

    public int PlannedMinutes => _items.Sum(i => i.DurationMinutes);

    // Alta nueva en la plantilla después de preparar la sesión: se le cuenta como esperado.
    public void EnsureAttendanceFor(IEnumerable<SquadPlayer> players, DateTimeOffset now)
    {
        foreach (var player in players)
        {
            if (!player.IsActive || _attendance.Any(a => a.SquadPlayerId == player.Id)) continue;
            _attendance.Add(TrainingAttendance.Expected(Id, player.Id, now));
        }
    }

    public TrainingAttendance SetAttendance(Guid squadPlayerId, AttendanceStatus status, string? reason, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
            throw new DomainException(ErrorCode.TrainingAttendanceInvalid);

        var entry = _attendance.FirstOrDefault(a => a.SquadPlayerId == squadPlayerId)
            ?? throw new DomainException(ErrorCode.SquadPlayerNotMember);
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
        };
    }
}

public sealed class TrainingAttendance : Entity
{
    public Guid SessionId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TrainingAttendance() { }

    internal static TrainingAttendance Expected(Guid sessionId, Guid squadPlayerId, DateTimeOffset now) => new()
    {
        Id = NewId(),
        SessionId = sessionId,
        SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId)),
        Status = AttendanceStatus.Expected,
        UpdatedAt = now,
    };

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
    Task<IReadOnlyList<TrainingSession>> GetSeriesAsync(Guid seriesId, DateTimeOffset from, CancellationToken cancellationToken = default);
    void Add(TrainingSession session);
    void Remove(TrainingSession session);
}
