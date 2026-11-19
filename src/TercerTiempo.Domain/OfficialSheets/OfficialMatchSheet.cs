using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.OfficialSheets;

public sealed class OfficialMatchSheet : AggregateRoot
{
    private readonly List<OfficialSheetLine> _lines = [];
    private readonly List<OfficialSheetEvent> _events = [];
    private readonly List<OfficialSheetStaff> _staff = [];

    public Guid MatchId { get; private set; }
    public string Provider { get; private set; } = string.Empty;

    public string ExternalId { get; private set; } = string.Empty;

    public OfficialSheetStatus Status { get; private set; }
    public string? SourceUrl { get; private set; }
    public string? Referee { get; private set; }
    public string HomeTeamName { get; private set; } = string.Empty;
    public string AwayTeamName { get; private set; } = string.Empty;
    public int? HomeGoals { get; private set; }
    public int? AwayGoals { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public DateTimeOffset? FetchedAt { get; private set; }

    public IReadOnlyCollection<OfficialSheetLine> Lines => _lines.AsReadOnly();
    public IReadOnlyCollection<OfficialSheetEvent> Events => _events.AsReadOnly();

    // Cuerpo técnico de cada equipo tal como figura en el acta (entrenador, segundo, delegados, fisio, material...).
    public IReadOnlyCollection<OfficialSheetStaff> Staff => _staff.AsReadOnly();

    // Cuándo se leyó el cuerpo técnico del acta (aunque no trajera a nadie); null en las guardadas antes de leerlo.
    public DateTimeOffset? StaffReadAt { get; private set; }

    public bool IsAvailable => Status == OfficialSheetStatus.Available;

    public bool IsProvisional => Status == OfficialSheetStatus.Provisional;

    public bool HasLineups => _lines.Count > 0;

    private OfficialMatchSheet() { }

    public static OfficialMatchSheet Create(Guid matchId, string provider, string externalId, string? sourceUrl, DateTimeOffset now) =>
        new()
        {
            Id = NewId(),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            Provider = Guard.NotEmpty(provider, nameof(provider), 50),
            ExternalId = Guard.NotEmpty(externalId, nameof(externalId), 200),
            SourceUrl = sourceUrl,
            Status = OfficialSheetStatus.Pending,
            CheckedAt = now,
        };

    public void MarkChecked(DateTimeOffset now) => CheckedAt = now;

    // El acta pública puede venir de otra fuente (otra web) si en la anterior no está o sigue pendiente.
    public void LinkSource(string provider, string externalId, string? sourceUrl)
    {
        Provider = Guard.NotEmpty(provider, nameof(provider), 50);
        ExternalId = Guard.NotEmpty(externalId, nameof(externalId), 200);
        SourceUrl = sourceUrl;
    }

    // closed = false: la web ya publica las alineaciones, pero el árbitro aún no ha cerrado el acta (faltan goles, cambios
    // o tarjetas). Se guarda como provisional, sin marcador, y no cuenta para minutos ni estadísticas.
    public void Apply(OfficialSheetData data, DateTimeOffset now, bool closed = true)
    {
        Referee = data.Referee;
        HomeTeamName = Guard.NotEmpty(data.HomeTeamName, "equipo local", 150);
        AwayTeamName = Guard.NotEmpty(data.AwayTeamName, "equipo visitante", 150);
        HomeGoals = closed ? data.HomeGoals : null;
        AwayGoals = closed ? data.AwayGoals : null;

        _lines.Clear();
        _events.Clear();

        foreach (var line in data.Lines)
            _lines.Add(OfficialSheetLine.Create(Id, line));

        var order = 0;
        foreach (var item in data.Events)
            _events.Add(OfficialSheetEvent.Create(Id, item, order++));

        if (data.Staff is { } staff) ApplyStaff(staff, now);

        Status = _lines.Count == 0 ? OfficialSheetStatus.Pending
            : closed ? OfficialSheetStatus.Available
            : OfficialSheetStatus.Provisional;
        CheckedAt = now;
        if (_lines.Count > 0)
            FetchedAt = now;
    }

    public IEnumerable<OfficialSheetLine> LinesOf(MatchSide side) => _lines.Where(l => l.Side == side);

    // Solo el cuerpo técnico: para las actas guardadas antes de leerlo, releyendo la página ya descargada.
    public void ApplyStaff(IReadOnlyList<OfficialStaffData> staff, DateTimeOffset now)
    {
        _staff.Clear();
        var order = 0;
        foreach (var member in staff)
        {
            if (string.IsNullOrWhiteSpace(member.FullName) || OfficialStaffRoles.NotAPerson(member.FullName)) continue;
            _staff.Add(OfficialSheetStaff.Create(Id, member, order++));
        }
        StaffReadAt = now;
    }

    public IEnumerable<OfficialSheetStaff> StaffOf(MatchSide side) => _staff.Where(s => s.Side == side).OrderBy(s => s.Order);
}

public enum OfficialSheetStatus
{
    Pending = 1,
    Available = 2,
    Provisional = 3,
}

public enum MatchSide
{
    Home = 1,
    Away = 2,
}

public enum OfficialEventType
{
    Goal = 1,
    PenaltyGoal = 2,
    OwnGoal = 3,
    YellowCard = 4,
    SecondYellow = 5,
    RedCard = 6,
    Substitution = 7,
}

public sealed class OfficialSheetLine : Entity
{
    public Guid SheetId { get; private set; }
    public MatchSide Side { get; private set; }
    public int? ShirtNumber { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public bool Started { get; private set; }

    public int? MinuteIn { get; private set; }

    public int? MinuteOut { get; private set; }

    public int MinutesPlayed { get; private set; }
    public int Goals { get; private set; }
    public int PenaltyGoals { get; private set; }
    public int OwnGoals { get; private set; }
    public int YellowCards { get; private set; }
    public bool RedCard { get; private set; }

    public bool Played => MinuteIn is not null;

    private OfficialSheetLine() { }

    internal static OfficialSheetLine Create(Guid sheetId, OfficialLineData data)
    {
        var name = Guard.NotEmpty(data.FullName, "jugador", 150);

        return new OfficialSheetLine
        {
            Id = NewId(),
            SheetId = sheetId,
            Side = data.Side,
            ShirtNumber = data.ShirtNumber is > 0 and < 100 ? data.ShirtNumber : null,
            FullName = name,
            NormalizedName = NameNormalizer.Normalize(name.Replace(',', ' ')),
            Started = data.Started,
            MinuteIn = data.MinuteIn,
            MinuteOut = data.MinuteOut,
            MinutesPlayed = Guard.NotNegative(data.MinutesPlayed, "minutos"),
            Goals = Guard.NotNegative(data.Goals, "goles"),
            PenaltyGoals = Guard.NotNegative(data.PenaltyGoals, "goles de penalti"),
            OwnGoals = Guard.NotNegative(data.OwnGoals, "goles en propia puerta"),
            YellowCards = Guard.InRange(data.YellowCards, 0, 2, "amarillas"),
            RedCard = data.RedCard,
        };
    }
}

public sealed class OfficialSheetEvent : Entity
{
    public Guid SheetId { get; private set; }
    public int Order { get; private set; }
    public MatchSide Side { get; private set; }
    public OfficialEventType Type { get; private set; }
    public int Minute { get; private set; }
    public int? ExtraMinute { get; private set; }
    public string PlayerName { get; private set; } = string.Empty;

    public string? RelatedPlayerName { get; private set; }

    private OfficialSheetEvent() { }

    internal static OfficialSheetEvent Create(Guid sheetId, OfficialEventData data, int order)
    {
        if (!Enum.IsDefined(data.Type))
            throw new DomainException(ErrorCode.OfficialSheetInvalidEvent);

        return new OfficialSheetEvent
        {
            Id = NewId(),
            SheetId = sheetId,
            Order = order,
            Side = data.Side,
            Type = data.Type,
            Minute = Guard.InRange(data.Minute, 0, 130, "minuto"),
            ExtraMinute = data.ExtraMinute,
            PlayerName = Guard.NotEmpty(data.PlayerName, "jugador", 150),
            RelatedPlayerName = string.IsNullOrWhiteSpace(data.RelatedPlayerName) ? null : data.RelatedPlayerName.Trim(),
        };
    }
}

public sealed record OfficialSheetData(
    string? Referee,
    string HomeTeamName,
    string AwayTeamName,
    int? HomeGoals,
    int? AwayGoals,
    IReadOnlyList<OfficialLineData> Lines,
    IReadOnlyList<OfficialEventData> Events,
    // Equipo arbitral completo (árbitro, asistentes y cuarto árbitro si lo hay).
    IReadOnlyList<Referees.MatchOfficialData>? Officials = null,
    // Cuerpo técnico de los dos equipos; null si la fuente no lo publica (entonces no se toca el guardado).
    IReadOnlyList<OfficialStaffData>? Staff = null);

// Una persona del cuerpo técnico en el acta: la función tal como la escribe la federación («2ºEntrenador», «DEL. Campo»)
// y el nombre en «APELLIDOS, NOMBRE».
public sealed record OfficialStaffData(MatchSide Side, string Role, string FullName);

public enum OfficialStaffRole
{
    HeadCoach = 1,
    AssistantCoach = 2,
    GoalkeeperCoach = 3,
    FitnessCoach = 4,
    TeamDelegate = 5,
    FieldDelegate = 6,
    Physio = 7,
    Medical = 8,
    KitManager = 9,
    Other = 99,
}

public static class OfficialStaffRoles
{
    // Las funciones que escribe asturfutbol.es en el «Cuerpo Técnico» del acta: «Entrenador», «2ºEntrenador», «DEL. Equipo»,
    // «DEL. Campo», «Fisioterapeuta», «Encargado Material»... Lo que no se reconoce queda como «Otro» con su texto.
    public static OfficialStaffRole FromLabel(string label)
    {
        var text = NameNormalizer.Normalize(label).Replace("º", " ").Replace("ª", " ").Replace(":", " ").Trim();
        bool Has(string word) => text.Contains(word, StringComparison.Ordinal);

        if (Has("portero")) return OfficialStaffRole.GoalkeeperCoach;
        if (Has("fisic") || Has("prep")) return OfficialStaffRole.FitnessCoach;
        if (Has("entrenador") || Has("tecnico"))
            return text.StartsWith('2') || Has("segundo") || Has("ayudante") || Has("auxiliar") || Has("practicas")
                ? OfficialStaffRole.AssistantCoach
                : OfficialStaffRole.HeadCoach;
        if (Has("del") && Has("campo")) return OfficialStaffRole.FieldDelegate;
        if (Has("delegado") || Has("del equipo") || text.StartsWith("del ", StringComparison.Ordinal)) return OfficialStaffRole.TeamDelegate;
        if (Has("fisio")) return OfficialStaffRole.Physio;
        if (Has("medic") || Has("ats") || Has("due") || Has("enfermer") || Has("sanitari")) return OfficialStaffRole.Medical;
        if (Has("material") || Has("utiller")) return OfficialStaffRole.KitManager;
        return OfficialStaffRole.Other;
    }

    // Cuando un equipo no lleva entrenador, el acta pone «No presenta» en su sitio: no es nadie.
    public static bool NotAPerson(string fullName)
    {
        var name = NameNormalizer.Normalize(fullName.Replace(',', ' '));
        return name.StartsWith("no present", StringComparison.Ordinal) || name is "sin entrenador" or "ninguno" or "no";
    }

    // La función equivalente en el cuerpo técnico de una plantilla de la app.
    public static StaffRole ToStaffRole(this OfficialStaffRole role) => role switch
    {
        OfficialStaffRole.HeadCoach => StaffRole.HeadCoach,
        OfficialStaffRole.AssistantCoach or OfficialStaffRole.GoalkeeperCoach or OfficialStaffRole.FitnessCoach => StaffRole.AssistantCoach,
        OfficialStaffRole.TeamDelegate or OfficialStaffRole.FieldDelegate => StaffRole.Delegate,
        OfficialStaffRole.Physio or OfficialStaffRole.Medical => StaffRole.Physio,
        _ => StaffRole.Other,
    };
}

public sealed class OfficialSheetStaff : Entity
{
    public Guid SheetId { get; private set; }
    public int Order { get; private set; }
    public MatchSide Side { get; private set; }
    public OfficialStaffRole Role { get; private set; }

    // La función tal como la escribe la federación.
    public string RoleLabel { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;

    private OfficialSheetStaff() { }

    internal static OfficialSheetStaff Create(Guid sheetId, OfficialStaffData data, int order)
    {
        var name = Guard.NotEmpty(data.FullName, "miembro del cuerpo técnico", 150);
        var label = string.IsNullOrWhiteSpace(data.Role) ? "Cuerpo técnico" : data.Role.Trim().TrimEnd(':').Trim();

        return new OfficialSheetStaff
        {
            Id = NewId(),
            SheetId = sheetId,
            Order = order,
            Side = data.Side,
            Role = OfficialStaffRoles.FromLabel(label),
            RoleLabel = label.Length > 60 ? label[..60] : label,
            FullName = name,
            NormalizedName = NameNormalizer.Normalize(name.Replace(',', ' ')),
        };
    }
}

public sealed record OfficialLineData(
    MatchSide Side,
    int? ShirtNumber,
    string FullName,
    bool Started,
    int? MinuteIn,
    int? MinuteOut,
    int MinutesPlayed,
    int Goals,
    int PenaltyGoals,
    int OwnGoals,
    int YellowCards,
    bool RedCard);

public sealed record OfficialEventData(
    MatchSide Side,
    OfficialEventType Type,
    int Minute,
    int? ExtraMinute,
    string PlayerName,
    string? RelatedPlayerName);
