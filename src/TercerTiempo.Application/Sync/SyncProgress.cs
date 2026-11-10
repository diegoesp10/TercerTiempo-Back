namespace TercerTiempo.Application.Sync;

public sealed record SyncStatusDto(
    bool Running,
    string? Phase,
    string? Current,
    int Done,
    int Total,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastFinishedAt,
    string? LastSummary,
    IReadOnlyList<string> LastErrors,
    DateTimeOffset? NextRunAt);

public static class SyncLeagueStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}

// Lo que va pasando en la descarga de las ligas, para verlo en directo en la administración: cada liga con su fase y
// sus cifras, y los últimos partidos, equipos, clasificaciones y plantillas tocados (con sus escudos).
public sealed record SyncLeagueDto(string Name, string Status, string? Stage, int Teams, int Matches, int Played, int Standings, int Roster, string? Error);

public sealed record SyncTeamRefDto(Guid Id, string Name, string? CrestUrl);

public sealed record SyncEventDto(
    long Id,
    DateTimeOffset At,
    string Kind,
    string League,
    string Text,
    SyncTeamRefDto? Home,
    SyncTeamRefDto? Away,
    int? HomeGoals,
    int? AwayGoals,
    int? Matchday,
    DateTimeOffset? KickoffAt,
    Guid? MatchId,
    string? Change);

public sealed record SyncLiveDto(
    bool Running,
    string? Scope,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Summary,
    int Done,
    int Total,
    string? Current,
    IReadOnlyList<SyncLeagueDto> Leagues,
    IReadOnlyList<SyncEventDto> Feed,
    IReadOnlyList<string> Errors);

public static class SyncEventKind
{
    public const string Match = "match";
    public const string Team = "team";
    public const string Standings = "standings";
    public const string Roster = "roster";
}

public interface ISyncProgress
{
    void Start(string phase, int total);
    void Step(string current, int done);
    void Finish(string summary, IReadOnlyList<string> errors);
    void ScheduleNext(DateTimeOffset? at);
    SyncStatusDto Snapshot();

    void Describe(string scope, IReadOnlyList<string> leagues);
    void League(string name, Func<SyncLeagueDto, SyncLeagueDto> update);
    void Event(string kind, string league, string text, SyncTeamRefDto? home = null, SyncTeamRefDto? away = null,
        int? homeGoals = null, int? awayGoals = null, int? matchday = null, DateTimeOffset? kickoffAt = null, Guid? matchId = null, string? change = null);
    SyncLiveDto Live();
}

internal sealed class SyncProgress(TimeProvider clock) : ISyncProgress
{
    private const int FeedSize = 400;

    private readonly Lock _lock = new();
    private bool _running;
    private string? _phase;
    private string? _current;
    private int _done;
    private int _total;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastFinishedAt;
    private string? _lastSummary;
    private IReadOnlyList<string> _lastErrors = [];
    private DateTimeOffset? _nextRunAt;
    private string? _scope;
    private readonly List<SyncLeagueDto> _leagues = [];
    private readonly LinkedList<SyncEventDto> _feed = new();
    private long _nextEventId;

    public void Start(string phase, int total)
    {
        lock (_lock)
        {
            _running = true;
            _phase = phase;
            _current = null;
            _done = 0;
            _total = total;
            _startedAt = clock.GetUtcNow();
            _lastErrors = [];
            _lastSummary = null;
        }
    }

    public void Step(string current, int done)
    {
        lock (_lock)
        {
            _current = current;
            _done = done;
        }
    }

    public void Finish(string summary, IReadOnlyList<string> errors)
    {
        lock (_lock)
        {
            _running = false;
            _current = null;
            _done = _total;
            _lastFinishedAt = clock.GetUtcNow();
            _lastSummary = summary;
            _lastErrors = errors;
            // Lo que se quedó a medias (la API se paró o falló el proveedor) no sigue «en marcha».
            for (var i = 0; i < _leagues.Count; i++)
                if (_leagues[i].Status == SyncLeagueStatus.Running) _leagues[i] = _leagues[i] with { Status = SyncLeagueStatus.Failed, Stage = null };
        }
    }

    public void ScheduleNext(DateTimeOffset? at)
    {
        lock (_lock) _nextRunAt = at;
    }

    public SyncStatusDto Snapshot()
    {
        lock (_lock)
            return new SyncStatusDto(_running, _phase, _current, _done, _total, _startedAt, _lastFinishedAt, _lastSummary, _lastErrors, _nextRunAt);
    }

    public void Describe(string scope, IReadOnlyList<string> leagues)
    {
        lock (_lock)
        {
            _scope = scope;
            _leagues.Clear();
            _leagues.AddRange(leagues.Select(name => new SyncLeagueDto(name, SyncLeagueStatus.Pending, null, 0, 0, 0, 0, 0, null)));
            _feed.Clear();
        }
    }

    public void League(string name, Func<SyncLeagueDto, SyncLeagueDto> update)
    {
        lock (_lock)
        {
            var index = _leagues.FindIndex(l => l.Name == name);
            if (index < 0)
            {
                _leagues.Add(update(new SyncLeagueDto(name, SyncLeagueStatus.Pending, null, 0, 0, 0, 0, 0, null)));
                return;
            }
            _leagues[index] = update(_leagues[index]);
        }
    }

    public void Event(string kind, string league, string text, SyncTeamRefDto? home = null, SyncTeamRefDto? away = null,
        int? homeGoals = null, int? awayGoals = null, int? matchday = null, DateTimeOffset? kickoffAt = null, Guid? matchId = null, string? change = null)
    {
        lock (_lock)
        {
            _feed.AddFirst(new SyncEventDto(
                ++_nextEventId, clock.GetUtcNow(), kind, league, text, home, away, homeGoals, awayGoals, matchday, kickoffAt, matchId, change));
            while (_feed.Count > FeedSize) _feed.RemoveLast();
        }
    }

    public SyncLiveDto Live()
    {
        lock (_lock)
            return new SyncLiveDto(
                _running, _scope, _startedAt, _running ? null : _lastFinishedAt, _running ? null : _lastSummary,
                _done, _total, _current, _leagues.ToList(), _feed.ToList(), _lastErrors);
    }
}
