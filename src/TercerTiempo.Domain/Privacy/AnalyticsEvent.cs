using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Privacy;

public enum AnalyticsEventType
{
    PageView = 1,
    Search = 2,
    Click = 3,
}

// Quién hace algo en la web: anónimo o con cuenta (y de qué tipo).
public enum AnalyticsActor
{
    Anonymous = 1,
    Player = 2,
    Coach = 3,
    Club = 4,
}

// De qué es la página que se ve o se toca.
public enum AnalyticsEntity
{
    Team = 1,
    Club = 2,
    Competition = 3,
    Match = 4,
    Squad = 5,
}

// Lo que hace un visitante que ha aceptado la analítica: ver una página, buscar o pulsar algo. Va con el id anónimo de
// su navegador y de su visita, quién era (y su club) y, si la página es de un equipo, liga, partido o plantilla, de cuál
// y de qué club. Se guarda 13 meses.
public sealed class AnalyticsEvent
{
    public const int MaxPath = 300;
    public const int MaxLabel = 200;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(395);

    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string VisitorId { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public AnalyticsEventType Type { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string? RouteName { get; private set; }
    public string? Label { get; private set; }
    public AnalyticsEntity? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }
    public AnalyticsActor Actor { get; private set; }
    public Guid? ActorClubId { get; private set; }
    public Guid? SubjectClubId { get; private set; }

    private AnalyticsEvent() { }

    public static AnalyticsEvent Create(
        DateTimeOffset occurredAt, string visitorId, string sessionId, AnalyticsEventType type, string path, string? routeName,
        string? label, AnalyticsEntity? entityType, Guid? entityId, AnalyticsActor actor, Guid? actorClubId, Guid? subjectClubId) =>
        new()
        {
            OccurredAt = occurredAt,
            VisitorId = visitorId.ToLowerInvariant(),
            SessionId = sessionId.ToLowerInvariant(),
            Type = type,
            Path = Trim(path, MaxPath) ?? "/",
            RouteName = Trim(routeName, 60),
            Label = Trim(label, MaxLabel),
            EntityType = entityType,
            EntityId = entityType is null ? null : entityId,
            Actor = actor,
            ActorClubId = actorClubId,
            SubjectClubId = subjectClubId,
        };

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..max];
    }
}

public interface IAnalyticsStore
{
    void AddRange(IEnumerable<AnalyticsEvent> events);
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
