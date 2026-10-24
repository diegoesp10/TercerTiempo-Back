using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.OfficialSheets;

public sealed class OfficialMatchSheet : AggregateRoot
{
    private readonly List<OfficialSheetLine> _lines = [];
    private readonly List<OfficialSheetEvent> _events = [];

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

    public bool IsAvailable => Status == OfficialSheetStatus.Available;

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

    public void Apply(OfficialSheetData data, DateTimeOffset now)
    {
        Referee = data.Referee;
        HomeTeamName = Guard.NotEmpty(data.HomeTeamName, "equipo local", 150);
        AwayTeamName = Guard.NotEmpty(data.AwayTeamName, "equipo visitante", 150);
        HomeGoals = data.HomeGoals;
        AwayGoals = data.AwayGoals;

        _lines.Clear();
        _events.Clear();

        foreach (var line in data.Lines)
            _lines.Add(OfficialSheetLine.Create(Id, line));

        var order = 0;
        foreach (var item in data.Events)
            _events.Add(OfficialSheetEvent.Create(Id, item, order++));

        Status = _lines.Count > 0 ? OfficialSheetStatus.Available : OfficialSheetStatus.Pending;
        CheckedAt = now;
        if (Status == OfficialSheetStatus.Available)
            FetchedAt = now;
    }

    public IEnumerable<OfficialSheetLine> LinesOf(MatchSide side) => _lines.Where(l => l.Side == side);
}

public enum OfficialSheetStatus
{
    Pending = 1,
    Available = 2,
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
    IReadOnlyList<OfficialEventData> Events);

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
