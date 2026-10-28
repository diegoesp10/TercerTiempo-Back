using TercerTiempo.Application.Sync;

namespace TercerTiempo.Application.Jobs;

public static class JobEventTone
{
    public const string Info = "info";
    public const string Success = "success";
    public const string Warning = "warning";
}

public sealed record JobEventDto(long Id, DateTimeOffset At, string Text, string Tone, SyncTeamRefDto? Home, SyncTeamRefDto? Away, Guid? MatchId);

public sealed record JobProgressSnapshot(bool Running, DateTimeOffset? StartedAt, string? Current, int Done, int Total, IReadOnlyList<JobEventDto> Feed);

// Lo que va haciendo cada trabajo en segundo plano, para verlo en directo en la administración: el paso en curso,
// cuánto lleva y lo último que ha tocado. Las ligas tienen además su propio detalle (ISyncProgress). El planificador
// abre y cierra cada ejecución; el trabajo solo cuenta sus pasos y sus novedades.
public interface IJobProgress
{
    void Start(string job);
    void Step(string job, string? current, int done, int total);
    void Event(string job, string text, string tone = JobEventTone.Info, SyncTeamRefDto? home = null, SyncTeamRefDto? away = null, Guid? matchId = null);
    void Finish(string job);
    JobProgressSnapshot Snapshot(string job);
}

internal sealed class JobProgress(TimeProvider clock) : IJobProgress
{
    private const int FeedSize = 300;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, State> _jobs = [];
    private long _nextEventId;

    public void Start(string job)
    {
        lock (_lock)
        {
            var state = Get(job);
            state.Running = true;
            state.StartedAt = clock.GetUtcNow();
            state.Current = null;
            state.Done = 0;
            state.Total = 0;
            state.Feed.Clear();
        }
    }

    public void Step(string job, string? current, int done, int total)
    {
        lock (_lock)
        {
            var state = Get(job);
            state.Current = current;
            state.Done = done;
            state.Total = total;
        }
    }

    public void Event(string job, string text, string tone = JobEventTone.Info, SyncTeamRefDto? home = null, SyncTeamRefDto? away = null, Guid? matchId = null)
    {
        lock (_lock)
        {
            var feed = Get(job).Feed;
            feed.AddFirst(new JobEventDto(++_nextEventId, clock.GetUtcNow(), text, tone, home, away, matchId));
            while (feed.Count > FeedSize) feed.RemoveLast();
        }
    }

    // Lo contado se queda a la vista hasta la siguiente ejecución.
    public void Finish(string job)
    {
        lock (_lock)
        {
            var state = Get(job);
            state.Running = false;
            state.Current = null;
        }
    }

    public JobProgressSnapshot Snapshot(string job)
    {
        lock (_lock)
        {
            var state = Get(job);
            return new JobProgressSnapshot(state.Running, state.StartedAt, state.Current, state.Done, state.Total, state.Feed.ToList());
        }
    }

    private State Get(string job)
    {
        if (!_jobs.TryGetValue(job, out var state)) _jobs[job] = state = new State();
        return state;
    }

    private sealed class State
    {
        public bool Running;
        public DateTimeOffset? StartedAt;
        public string? Current;
        public int Done;
        public int Total;
        public readonly LinkedList<JobEventDto> Feed = new();
    }
}
