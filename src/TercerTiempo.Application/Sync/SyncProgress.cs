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

public interface ISyncProgress
{
    void Start(string phase, int total);
    void Step(string current, int done);
    void Finish(string summary, IReadOnlyList<string> errors);
    void ScheduleNext(DateTimeOffset? at);
    SyncStatusDto Snapshot();
}

internal sealed class SyncProgress(TimeProvider clock) : ISyncProgress
{
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
}
