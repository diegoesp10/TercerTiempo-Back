using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Infrastructure.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Infrastructure.Jobs;

// Lo que enseña /jobs. Las descargas (su historial, el registro de llamadas y el estado de cada web) están en la base de
// datos reales; los demás trabajos, en la del entorno.
internal sealed class JobsMonitor(
    JobScheduler scheduler,
    IServiceScopeFactory scopes,
    IRealDataLease lease,
    ISyncProgress syncProgress,
    IJobProgress jobProgress,
    OfficialSheetQueue sheetQueue,
    IOptions<OfficialSheetOptions> sheetOptions,
    TimeProvider clock) : IJobsMonitor
{
    private const int RunsShown = 60;

    public JobLaunchResult Launch(string job, object? parameters = null) => scheduler.Launch(job, parameters);

    public Task<DateTimeOffset?> LastFullFootballSyncAsync(CancellationToken cancellationToken) =>
        RealDataAsync(db => db.Set<JobRun>().AsNoTracking()
            .Where(r => r.Job == JobNames.FootballData
                && r.ScopeKind == nameof(SyncScopeKind.FullSeason)
                && (r.Status == JobRunStatus.Succeeded || r.Status == JobRunStatus.Partial))
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken));

    public async Task<ScrapeSourceDto?> SourceAsync(string provider, CancellationToken cancellationToken) =>
        (await SourcesAsync(clock.GetUtcNow(), cancellationToken)).FirstOrDefault(s => s.Provider == provider);

    public async Task<JobsOverviewDto> GetOverviewAsync(string? provider, int requests, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var runs = await RecentRunsAsync(cancellationToken);
        var sources = await SourcesAsync(now, cancellationToken);
        var jobs = await JobsAsync(runs, sources, now, cancellationToken);

        var recent = await RealDataAsync(db =>
        {
            var query = db.Set<ScrapeRequest>().AsNoTracking();
            if (provider is not null) query = query.Where(r => r.Provider == provider);
            return query
                .OrderByDescending(r => r.Id)
                .Take(requests)
                .Select(r => new ScrapeRequestDto(r.Id, r.At, r.Provider, r.Method, r.Url, r.StatusCode, r.DurationMs, r.Error, r.JobRunId))
                .ToListAsync(cancellationToken);
        });

        return new JobsOverviewDto(jobs, sources, runs, recent, now, await IngesterAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<JobLiveDto>> LiveAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var jobs = await JobsAsync(await RecentRunsAsync(cancellationToken), await SourcesAsync(now, cancellationToken), now, cancellationToken);
        return jobs
            .Select(job =>
            {
                var live = jobProgress.Snapshot(job.Name);
                return new JobLiveDto(job, live.StartedAt, live.Feed);
            })
            .ToList();
    }

    private async Task<RealDataIngesterDto?> IngesterAsync(CancellationToken cancellationToken)
    {
        var ingester = await RealDataAsync(db => db.Set<RealDataIngester>().AsNoTracking().FirstOrDefaultAsync(cancellationToken));
        // Sin latido reciente nadie tiene el bloqueo: la API que lo tenía se paró sin poder decirlo.
        if (ingester is null || clock.GetUtcNow() - ingester.HeartbeatAt > TimeSpan.FromMinutes(2)) return null;
        return new RealDataIngesterDto(ingester.Environment, ingester.Machine, ingester.Since, ingester.HeartbeatAt, lease.IsHeld, lease.CanIngest);
    }

    private async Task<List<JobRunDto>> RecentRunsAsync(CancellationToken cancellationToken)
    {
        var realDataJobs = scheduler.Definitions.Where(d => d.RealData).Select(d => d.Name).ToList();

        var downloads = await RealDataAsync(async db =>
        {
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
            return runs;
        });

        var local = await EnvironmentAsync(async db => (await db.Set<JobRun>().AsNoTracking()
                .Where(r => !realDataJobs.Contains(r.Job))
                .OrderByDescending(r => r.StartedAt)
                .Take(RunsShown)
                .ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToList());

        return downloads.Concat(local).OrderByDescending(r => r.StartedAt).Take(RunsShown).ToList();
    }

    private async Task<List<JobDto>> JobsAsync(
        IReadOnlyList<JobRunDto> runs, IReadOnlyList<ScrapeSourceDto> sources, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Las actas esperan a la federación: con la pausa a la vista se entiende por qué lanzarlas no empieza ya.
        var federation = sources.FirstOrDefault(s => s.Provider == AsturfutbolOfficialSheetProvider.Name);
        var sheetsPause = FederationPause.From(federation, sheetQueue.PausedUntil, now);

        var jobs = new List<JobDto>();
        foreach (var definition in scheduler.Definitions)
        {
            Task<JobRunDto?> Last(AppDbContext db) => db.Set<JobRun>().AsNoTracking()
                .Where(r => r.Job == definition.Name)
                .OrderByDescending(r => r.StartedAt)
                .Select(r => ToDto(r))
                .FirstOrDefaultAsync(cancellationToken);

            var lastRun = runs.FirstOrDefault(r => r.Job == definition.Name)
                ?? (definition.RealData ? await RealDataAsync(Last) : await EnvironmentAsync(Last));

            var (running, nextRunAt) = scheduler.State(definition.Name);
            // Lo que hace otro proceso se sabe por su historial: en marcha si su última ejecución sigue abierta.
            if (definition.External)
                running = lastRun is { Status: JobRunStatus.Running } && now - lastRun.StartedAt < TimeSpan.FromHours(12);
            JobProgressDto? progress = null;
            if (running && definition.Name == JobNames.FootballData && syncProgress.Snapshot() is { Running: true } sync)
                progress = new JobProgressDto(sync.Current, sync.Done, sync.Total);
            else if (running && jobProgress.Snapshot(definition.Name) is { Running: true } live && (live.Total > 0 || live.Current is not null))
                progress = new JobProgressDto(live.Current, live.Done, live.Total);

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
                definition.Weekly?.Describe(),
                definition.Name == JobNames.OfficialSheets && !running ? sheetsPause : null,
                definition.RealData,
                definition.External));
        }
        return jobs;
    }

    private async Task<IReadOnlyList<ScrapeSourceDto>> SourcesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.AddHours(-24);
        var (activity, states) = await RealDataAsync(async db => (
            await db.Set<ScrapeRequest>().AsNoTracking()
                .Where(r => r.At >= since)
                .GroupBy(r => r.Provider)
                .Select(g => new SourceActivity(
                    g.Key,
                    g.Count(),
                    g.Count(r => r.StatusCode == null || r.StatusCode >= 400),
                    g.Max(r => r.At)))
                .ToDictionaryAsync(x => x.Provider, cancellationToken),
            await db.Set<FederationRequestState>().AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken)));

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

    private async Task<T> RealDataAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = scopes.CreateRealDataScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Un ámbito propio: este monitor también se usa desde dentro de las descargas, cuyo AppDbContext va a la base real.
    private async Task<T> EnvironmentAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = scopes.CreateEnvironmentScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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
        run.ScopeLabel,
        run.Environment);

    private sealed record SourceActivity(string Provider, int Count, int Failed, DateTimeOffset Last);
}
