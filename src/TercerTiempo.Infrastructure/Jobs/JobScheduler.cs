using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Infrastructure.FootballData;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Jobs;

internal sealed record JobDefinition(
    string Name, string Title, string Description, TimeSpan? Interval, TimeSpan FirstRunDelay, bool Enabled, WeeklySlot? Weekly = null);

// Un día y una hora fijos de cada semana, en la hora de la federación.
internal sealed record WeeklySlot(DayOfWeek Day, TimeOnly At, TimeZoneInfo Zone)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public DateTimeOffset Next(DateTimeOffset after)
    {
        var local = TimeZoneInfo.ConvertTime(after, Zone).DateTime;
        var candidate = local.Date.AddDays(((int)Day - (int)local.DayOfWeek + 7) % 7) + At.ToTimeSpan();
        if (candidate <= local) candidate = candidate.AddDays(7);
        return SiteClock.ToUtc(candidate, Zone);
    }

    public string Describe() => $"Los {Spanish.DateTimeFormat.GetDayName(Day)} a las {At.ToString("H:mm", Spanish)}";
}

internal sealed record JobOutcome(string Summary, IReadOnlyList<string> Errors);

internal sealed record JobScope(string Kind, string Label);

// Ejecución en curso: las llamadas a webs externas hechas dentro de ella quedan apuntadas a su nombre.
internal static class JobContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? RunId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

// Cuándo toca cada trabajo en segundo plano. La hora de la última ejecución se lee de la base de datos, así que
// reiniciar la API no vuelve a lanzar los scrapeos: se espera a que pase el intervalo desde la anterior.
internal sealed class JobScheduler
{
    private static readonly TimeSpan RequestRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan RunRetention = TimeSpan.FromDays(180);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Slot> _slots;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobScheduler> _logger;

    public JobScheduler(
        IOptions<FootballDataOptions> football,
        IOptions<OfficialSheetOptions> sheets,
        IOptions<LicensingOptions> licensing,
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger<JobScheduler> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;

        var footballMinutes = football.Value.SyncIntervalMinutes;
        var sheetMinutes = sheets.Value.BackgroundScanMinutes;
        var weeklyDays = sheets.Value.WeeklyQueueDays;
        JobDefinition[] definitions =
        [
            new(JobNames.FootballData,
                "Datos de las ligas",
                $"Ligas, equipos, partidos, resultados, clasificaciones y plantillas desde el proveedor de datos ({football.Value.Provider}).",
                footballMinutes > 0 ? TimeSpan.FromMinutes(footballMinutes) : null,
                TimeSpan.FromSeconds(10),
                true),
            new(JobNames.OfficialSheets,
                "Actas públicas",
                $"Busca las actas que faltan o toca refrescar (hasta {sheets.Value.BackgroundBatch} por pasada) en asturfutbol.es, con su límite de peticiones.",
                sheetMinutes > 0 ? TimeSpan.FromMinutes(sheetMinutes) : null,
                TimeSpan.FromSeconds(Math.Max(0, sheets.Value.BackgroundStartDelaySeconds)),
                sheets.Value.Enabled),
            new(JobNames.LicenseRenewals,
                "Renovaciones de licencias",
                "Cobra (sin cobro real) el periodo siguiente de las suscripciones de licencia que llegan a su día de renovación. Solo con la pasarela simulada; con Stripe las renueva su webhook.",
                TimeSpan.FromMinutes(1440),
                TimeSpan.FromSeconds(60),
                licensing.Value.Provider == "mock"),
            new(JobNames.WeeklySheets,
                "Actas del fin de semana",
                $"Pone en cola las actas de los partidos de los últimos {weeklyDays} días de las ligas de la federación asturiana (regionales y 3ª RFEF); se descargan en tandas con su límite de peticiones.",
                weeklyDays > 0 ? TimeSpan.FromDays(7) : null,
                TimeSpan.FromSeconds(30),
                sheets.Value.Enabled && weeklyDays > 0,
                new WeeklySlot(sheets.Value.WeeklyQueueDay, sheets.Value.WeeklyQueueAt, SiteClock.Resolve(sheets.Value.Asturfutbol.TimeZone))),
        ];
        _slots = definitions.ToDictionary(d => d.Name, d => new Slot(d));
    }

    public IEnumerable<JobDefinition> Definitions => _slots.Values.Select(s => s.Definition);

    public JobDefinition Get(string job) => _slots[job].Definition;

    public (bool Running, DateTimeOffset? NextRunAt) State(string job)
    {
        var slot = _slots[job];
        lock (_lock) return (slot.Running, slot.Running ? null : slot.NextRunAt);
    }

    public JobLaunchResult Launch(string job, object? parameters = null)
    {
        if (!_slots.TryGetValue(job, out var slot)) return JobLaunchResult.NotFound;
        if (!slot.Definition.Enabled) return JobLaunchResult.Disabled;

        lock (_lock)
        {
            if (slot.Running) return JobLaunchResult.AlreadyRunning;
            slot.Triggered = true;
            slot.Parameters = parameters;
            slot.Signal.TrySetResult();
        }
        return JobLaunchResult.Accepted;
    }

    // Lo que se pidió al lanzarlo a mano (y se olvida al leerlo); null si fue a su hora o sin indicar nada.
    public object? TakeParameters(string job)
    {
        var slot = _slots[job];
        lock (_lock)
        {
            var parameters = slot.Parameters;
            slot.Parameters = null;
            return parameters;
        }
    }

    public Task WhenTriggered(string job)
    {
        lock (_lock) return _slots[job].Signal.Task;
    }

    public bool TryConsumeTrigger(string job)
    {
        var slot = _slots[job];
        lock (_lock)
        {
            if (!slot.Triggered) return false;
            slot.Triggered = false;
            slot.Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    public void ScheduleNext(string job, DateTimeOffset? at)
    {
        var slot = _slots[job];
        lock (_lock) slot.NextRunAt = at;
    }

    // Primera ejecución tras arrancar: un intervalo después de la última guardada (o enseguida si nunca se ha hecho).
    // Las que quedaron en marcha porque la API se paró a medias se cierran como interrumpidas.
    public async Task<DateTimeOffset?> FirstDueAsync(string job, CancellationToken cancellationToken)
    {
        var definition = Get(job);
        var now = _clock.GetUtcNow();

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<JobRun>()
            .Where(r => r.Job == job && r.Status == JobRunStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, JobRunStatus.Interrupted), cancellationToken);

        if (definition.Interval is not { } interval) return null;

        var lastStart = await db.Set<JobRun>()
            .Where(r => r.Job == job)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var earliest = now + definition.FirstRunDelay;
        // A día fijo: el siguiente tras la última ejecución; si se pasó con la API apagada, se hace al arrancar.
        if (definition.Weekly is { } weekly)
        {
            var next = weekly.Next(lastStart ?? now);
            return next > earliest ? next : earliest;
        }
        if (lastStart is null) return earliest;
        var due = lastStart.Value + interval;
        return due > earliest ? due : earliest;
    }

    // Espera a la hora prevista o a que se lance a mano desde /jobs; devuelve si ha sido a mano.
    public async Task<bool> WaitAsync(string job, DateTimeOffset? due, CancellationToken cancellationToken)
    {
        ScheduleNext(job, due);
        while (true)
        {
            if (TryConsumeTrigger(job)) return true;

            var signal = WhenTriggered(job);
            if (due is null)
            {
                await signal.WaitAsync(cancellationToken);
                continue;
            }

            var wait = due.Value - _clock.GetUtcNow();
            if (wait <= TimeSpan.Zero) return false;

            using var delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(signal, Task.Delay(wait, _clock, delay.Token));
            await delay.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public async Task RunAsync(
        string job, bool manual, Func<CancellationToken, Task<JobOutcome>> work, CancellationToken cancellationToken, JobScope? scope = null)
    {
        var slot = _slots[job];
        lock (_lock) slot.Running = true;

        var run = new JobRun
        {
            Id = Guid.CreateVersion7(),
            Job = job,
            Manual = manual,
            Status = JobRunStatus.Running,
            StartedAt = _clock.GetUtcNow(),
            ScopeKind = scope?.Kind,
            ScopeLabel = scope?.Label,
        };

        var status = JobRunStatus.Succeeded;
        string? summary = null;
        IReadOnlyList<string> errors = [];
        try
        {
            await SaveAsync(db => db.Add(run));
            JobContext.RunId = run.Id;

            var outcome = await work(cancellationToken);
            summary = outcome.Summary;
            errors = outcome.Errors;
            status = errors.Count == 0 ? JobRunStatus.Succeeded : JobRunStatus.Partial;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = JobRunStatus.Interrupted;
            summary = "Parada al apagar la API";
            throw;
        }
        catch (ProviderThrottledException ex)
        {
            status = JobRunStatus.Paused;
            summary = ex.Message;
            throw;
        }
        catch (Exception ex)
        {
            status = JobRunStatus.Failed;
            errors = [ex.Message];
            throw;
        }
        finally
        {
            JobContext.RunId = null;
            lock (_lock) slot.Running = false;
            await FinishAsync(run.Id, status, summary, errors);
        }
    }

    private async Task FinishAsync(Guid runId, string status, string? summary, IReadOnlyList<string> errors)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = _clock.GetUtcNow();

            var run = await db.Set<JobRun>().SingleOrDefaultAsync(r => r.Id == runId);
            if (run is not null)
            {
                run.Status = status;
                run.FinishedAt = now;
                run.Summary = summary is { Length: > 1000 } ? summary[..1000] : summary;
                run.Errors = errors.Count == 0 ? null : string.Join('\n', errors);
                run.Requests = await db.Set<ScrapeRequest>().CountAsync(r => r.JobRunId == runId);
                await db.SaveChangesAsync();
            }

            var requestCutoff = now - RequestRetention;
            var runCutoff = now - RunRetention;
            await db.Set<ScrapeRequest>().Where(r => r.At < requestCutoff).ExecuteDeleteAsync();
            await db.Set<JobRun>().Where(r => r.StartedAt < runCutoff).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar el final de la ejecución {RunId}", runId);
        }
    }

    private async Task SaveAsync(Action<AppDbContext> change)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    private sealed class Slot(JobDefinition definition)
    {
        public JobDefinition Definition { get; } = definition;
        public bool Running;
        public bool Triggered;
        public object? Parameters;
        public DateTimeOffset? NextRunAt;
        public TaskCompletionSource Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
