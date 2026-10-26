using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Jobs;

internal sealed class JobsMonitor(
    JobScheduler scheduler,
    AppDbContext db,
    ISyncProgress syncProgress,
    IOptions<OfficialSheetOptions> sheetOptions,
    TimeProvider clock) : IJobsMonitor
{
    private const int RunsShown = 60;

    public JobLaunchResult Launch(string job, object? parameters = null) => scheduler.Launch(job, parameters);

    public Task<DateTimeOffset?> LastFullFootballSyncAsync(CancellationToken cancellationToken) =>
        db.Set<JobRun>().AsNoTracking()
            .Where(r => r.Job == JobNames.FootballData
                && r.ScopeKind == nameof(SyncScopeKind.FullSeason)
                && (r.Status == JobRunStatus.Succeeded || r.Status == JobRunStatus.Partial))
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ScrapeSourceDto?> SourceAsync(string provider, CancellationToken cancellationToken) =>
        (await SourcesAsync(clock.GetUtcNow(), cancellationToken)).FirstOrDefault(s => s.Provider == provider);

    public async Task<JobsOverviewDto> GetOverviewAsync(string? provider, int requests, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var runs = (await db.Set<JobRun>().AsNoTracking()
                .OrderByDescending(r => r.StartedAt)
                .Take(RunsShown)
                .ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToList();

        // Las que siguen en marcha aún no tienen su total de llamadas guardado: se cuenta al vuelo.
        for (var i = 0; i < runs.Count; i++)
        {
            if (runs[i].Status != JobRunStatus.Running) continue;
            var runId = runs[i].Id;
            runs[i] = runs[i] with { Requests = await db.Set<ScrapeRequest>().CountAsync(r => r.JobRunId == runId, cancellationToken) };
        }

        var jobs = new List<JobDto>();
        foreach (var definition in scheduler.Definitions)
        {
            var lastRun = runs.FirstOrDefault(r => r.Job == definition.Name)
                ?? await db.Set<JobRun>().AsNoTracking()
                    .Where(r => r.Job == definition.Name)
                    .OrderByDescending(r => r.StartedAt)
                    .Select(r => ToDto(r))
                    .FirstOrDefaultAsync(cancellationToken);

            var (running, nextRunAt) = scheduler.State(definition.Name);
            var progress = running && definition.Name == JobNames.FootballData && syncProgress.Snapshot() is { Running: true } sync
                ? new JobProgressDto(sync.Current, sync.Done, sync.Total)
                : null;

            jobs.Add(new JobDto(
                definition.Name,
                definition.Title,
                definition.Description,
                definition.Interval is { } interval ? (int)interval.TotalMinutes : null,
                definition.Enabled,
                running,
                nextRunAt,
                progress,
                lastRun,
                definition.Weekly?.Describe()));
        }

        var recentQuery = db.Set<ScrapeRequest>().AsNoTracking();
        if (provider is not null) recentQuery = recentQuery.Where(r => r.Provider == provider);
        var recent = await recentQuery
            .OrderByDescending(r => r.Id)
            .Take(requests)
            .Select(r => new ScrapeRequestDto(r.Id, r.At, r.Provider, r.Method, r.Url, r.StatusCode, r.DurationMs, r.Error, r.JobRunId))
            .ToListAsync(cancellationToken);

        return new JobsOverviewDto(jobs, await SourcesAsync(now, cancellationToken), runs, recent, now);
    }

    private async Task<IReadOnlyList<ScrapeSourceDto>> SourcesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.AddHours(-24);
        var activity = await db.Set<ScrapeRequest>().AsNoTracking()
            .Where(r => r.At >= since)
            .GroupBy(r => r.Provider)
            .Select(g => new
            {
                Provider = g.Key,
                Count = g.Count(),
                Failed = g.Count(r => r.StatusCode == null || r.StatusCode >= 400),
                Last = g.Max(r => r.At),
            })
            .ToDictionaryAsync(x => x.Provider, cancellationToken);

        var states = await db.Set<FederationRequestState>().AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);

        var settings = sheetOptions.Value;
        var limits = new[] { settings.AsturfutbolLimits }.ToDictionary(l => l.Provider);

        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);

        return limits.Keys.Concat(states.Keys).Concat(activity.Keys)
            .Distinct()
            .Select(name =>
            {
                var state = states.GetValueOrDefault(name);
                var limit = limits.GetValueOrDefault(name);
                var seen = activity.GetValueOrDefault(name);
                return new ScrapeSourceDto(
                    name,
                    state is null ? (limit is null ? null : 0) : state.HourStartedAt == hour ? state.HourRequests : 0,
                    limit?.MaxRequestsPerHour,
                    state is null ? (limit is null ? null : 0) : state.DayStartedAt == day ? state.DayRequests : 0,
                    limit is { MaxRequestsPerDay: > 0 } ? limit.MaxRequestsPerDay : null,
                    state?.PausedUntil > now ? state.PausedUntil : null,
                    state?.NextRequestAt > now ? state.NextRequestAt : null,
                    seen?.Count ?? 0,
                    seen?.Failed ?? 0,
                    seen?.Last,
                    state?.PausedUntil > now ? state.PauseKind : null,
                    state?.PausedUntil > now ? state.PauseReason : null,
                    BurstInProgress(state, limit, now),
                    limit is { MaxRequestsPerBurst: > 0 } ? limit.MaxRequestsPerBurst : null,
                    limit is { MaxRequestsPerBurst: > 0 } ? limit.BurstRestMinutes : null,
                    limit?.RequestDelayMs);
            })
            .OrderBy(s => s.Provider)
            .ToList();
    }

    // La tanda sigue abierta mientras no haya pasado un descanso entero desde la última petición.
    private static int BurstInProgress(FederationRequestState? state, SourceLimits? limit, DateTimeOffset now) =>
        state is null || limit is not { MaxRequestsPerBurst: > 0 } || state.LastRequestAt is null
            || now - state.LastRequestAt >= TimeSpan.FromMinutes(Math.Max(1, limit.BurstRestMinutes))
            ? 0
            : state.BurstRequests;

    private static JobRunDto ToDto(JobRun run) => new(
        run.Id,
        run.Job,
        run.Manual,
        run.Status,
        run.StartedAt,
        run.FinishedAt,
        run.Summary,
        string.IsNullOrEmpty(run.Errors) ? [] : run.Errors.Split('\n'),
        run.Requests,
        run.ScopeLabel);
}
