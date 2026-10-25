using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;
using TercerTiempo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Infrastructure.OfficialSheets;

internal sealed class OfficialSheetQueue : IOfficialSheetQueue, ISheetDownloadSignal
{
    private readonly Lock _batchLock = new();
    private TaskCompletionSource _batch = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    public bool Enqueue(Guid matchId) => _queued.TryAdd(matchId, 0) && _channel.Writer.TryWrite(matchId);

    public bool IsQueued(Guid matchId) => _queued.ContainsKey(matchId);

    public bool TryDequeue(out Guid matchId) => _channel.Reader.TryRead(out matchId);

    public void Done(Guid matchId) => _queued.TryRemove(matchId, out _);

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);

    public DateTimeOffset? PausedUntil { get; set; }

    // Hay actas nuevas pedidas desde la administración (ver SheetDownloads).
    public void Signal()
    {
        lock (_batchLock) _batch.TrySetResult();
    }

    public Task WhenBatch()
    {
        lock (_batchLock) return _batch.Task;
    }

    public void ResetBatchSignal()
    {
        lock (_batchLock)
            if (_batch.Task.IsCompleted) _batch = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

// Descarga las actas públicas en segundo plano y las guarda en la base de datos, para que abrir un partido no espere a
// las webs externas. Atiende primero los partidos que piden los usuarios (cola), después las actas pedidas desde la
// administración (tabla SheetDownloads, que sobrevive a los reinicios) y, cada BackgroundScanMinutes desde la última
// pasada guardada (no al arrancar), recorre las temporadas en curso buscando actas que falten o toque refrescar.
internal sealed class OfficialSheetSyncWorker(
    IServiceScopeFactory scopeFactory,
    OfficialSheetQueue queue,
    JobScheduler scheduler,
    IOptions<OfficialSheetOptions> options,
    TimeProvider clock,
    ILogger<OfficialSheetSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;

        var job = scheduler.Get(JobNames.OfficialSheets);
        DateTimeOffset? nextScan;
        try
        {
            await RestoreStateAsync(stoppingToken);
            nextScan = await scheduler.FirstDueAsync(job.Name, stoppingToken);
            if (nextScan is not null) logger.LogInformation("Próxima pasada de actas: {Due}", nextScan);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainQueueAsync(stoppingToken);

                queue.ResetBatchSignal();
                if (await NextRequestedAsync(stoppingToken) is not null)
                    await scheduler.RunAsync(job.Name, true, DrainRequestedAsync, stoppingToken,
                        new JobScope("Requested", "Actas pedidas desde la administración"));

                var manual = scheduler.TryConsumeTrigger(job.Name);
                if (manual || clock.GetUtcNow() >= nextScan)
                {
                    scheduler.ScheduleNext(job.Name, null);
                    try
                    {
                        await scheduler.RunAsync(job.Name, manual, ct => ScanAsync(settings.BackgroundBatch, ct), stoppingToken);
                    }
                    finally
                    {
                        nextScan = job.Interval is { } interval ? clock.GetUtcNow() + interval : null;
                    }
                }
                scheduler.ScheduleNext(job.Name, nextScan);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                if (nextScan is { } at)
                {
                    var wait = at - clock.GetUtcNow();
                    if (wait <= TimeSpan.Zero) continue;
                    timeout.CancelAfter(wait);
                }

                var queued = queue.WaitAsync(timeout.Token).AsTask();
                await Task.WhenAny(queued, scheduler.WhenTriggered(job.Name), queue.WhenBatch());
                await timeout.CancelAsync();
                try { await queued; }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ProviderThrottledException ex)
            {
                var until = ex.RetryAt ?? clock.GetUtcNow().AddHours(24);
                logger.LogWarning("Descargas de actas aplazadas hasta {Until}: {Message}", until, ex.Message);
                queue.PausedUntil = until;
                var delay = until - clock.GetUtcNow();
                if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, stoppingToken);
                queue.PausedUntil = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en la descarga de actas en segundo plano");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    // Tras un reinicio: lo que quedó a medias vuelve a la cola y la pausa de la federación guardada en la base de datos se
    // conoce desde el principio (si no, abrir un partido diría que el acta se descarga enseguida).
    private async Task RestoreStateAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISheetDownloadStore>().ResetInterruptedAsync(cancellationToken);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var paused = await db.Set<FederationRequestState>().AsNoTracking()
            .Where(s => s.Id == AsturfutbolOfficialSheetProvider.Name)
            .Select(s => s.PausedUntil)
            .FirstOrDefaultAsync(cancellationToken);
        if (paused > clock.GetUtcNow()) queue.PausedUntil = paused;
    }

    private async Task<Guid?> NextRequestedAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISheetDownloadStore>().NextPendingAsync(cancellationToken);
    }

    private async Task MarkAsync(Guid matchId, string status, string? detail, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISheetDownloadStore>().MarkAsync(matchId, status, detail, clock.GetUtcNow(), cancellationToken);
    }

    // Las actas pedidas desde la administración, en su orden, siempre por detrás de lo que abren los usuarios. Si la
    // federación corta, el partido vuelve a la cola y la ejecución queda en pausa; se sigue al acabar la pausa.
    private async Task<JobOutcome> DrainRequestedAsync(CancellationToken cancellationToken)
    {
        var runId = JobContext.RunId;
        var counts = new Dictionary<string, int>();
        var errors = new List<string>();

        while (await NextRequestedAsync(cancellationToken) is { } matchId)
        {
            await DrainQueueAsync(cancellationToken);
            JobContext.RunId = runId;

            await MarkAsync(matchId, SheetDownloadStatus.Downloading, null, cancellationToken);
            string status;
            string? detail;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto>>();
                var sheet = await handler.Handle(new SyncOfficialSheetCommand(matchId, Background: true, FederationAllowed: true, StopWhenThrottled: true), cancellationToken);
                (status, detail) = Describe(sheet);
                if (status == SheetDownloadStatus.Failed) errors.Add($"Partido {matchId}: {detail}");
            }
            catch (ProviderThrottledException ex)
            {
                await MarkAsync(matchId, SheetDownloadStatus.Pending,
                    ex.RetryAt is { } until ? $"En espera: la federación está en pausa hasta {until.ToLocalTime():dd/MM HH:mm}" : "En espera de la federación",
                    CancellationToken.None);
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await MarkAsync(matchId, SheetDownloadStatus.Pending, null, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo descargar el acta pedida del partido {MatchId}", matchId);
                (status, detail) = (SheetDownloadStatus.Failed, ex.Message);
                errors.Add($"Partido {matchId}: {ex.Message}");
            }

            await MarkAsync(matchId, status, detail, cancellationToken);
            counts[status] = counts.GetValueOrDefault(status) + 1;
        }

        var done = counts.GetValueOrDefault(SheetDownloadStatus.Done);
        var missing = counts.GetValueOrDefault(SheetDownloadStatus.Missing);
        var failed = counts.GetValueOrDefault(SheetDownloadStatus.Failed);
        return new JobOutcome($"{done} actas guardadas, {missing} aún sin publicar, {failed} con error", errors);
    }

    private static (string Status, string? Detail) Describe(OfficialSheetDto sheet)
    {
        if (sheet.Status == OfficialSheetAvailability.Pending && sheet.Message is null)
            return (SheetDownloadStatus.Missing, "Ya se comprobó hace menos de un día y aún no estaba publicada");
        if (sheet.Status == OfficialSheetAvailability.Pending && sheet.Message?.StartsWith("No se ha podido", StringComparison.Ordinal) == true)
            return (SheetDownloadStatus.Failed, sheet.Message);
        if (sheet.Status != OfficialSheetAvailability.Available)
            return (SheetDownloadStatus.Missing, sheet.Message ?? "La federación aún no la ha publicado");

        var players = (sheet.Home?.Starters.Count ?? 0) + (sheet.Home?.Bench.Count ?? 0) + (sheet.Away?.Starters.Count ?? 0) + (sheet.Away?.Bench.Count ?? 0);
        var goals = sheet.Events.Count(e => e.Type is OfficialEventType.Goal or OfficialEventType.PenaltyGoal or OfficialEventType.OwnGoal);
        var cards = sheet.Events.Count(e => e.Type is OfficialEventType.YellowCard or OfficialEventType.SecondYellow or OfficialEventType.RedCard);
        List<string> parts = [$"{players} jugadores"];
        if (goals > 0) parts.Add(goals == 1 ? "1 gol" : $"{goals} goles");
        if (cards > 0) parts.Add(cards == 1 ? "1 tarjeta" : $"{cards} tarjetas");
        if (!string.IsNullOrWhiteSpace(sheet.Referee)) parts.Add($"árbitro {sheet.Referee}");
        return (SheetDownloadStatus.Done, string.Join(" · ", parts));
    }

    private async Task DrainQueueAsync(CancellationToken cancellationToken)
    {
        // Lo que piden los usuarios no cuenta como parte de la pasada en curso.
        JobContext.RunId = null;
        while (queue.TryDequeue(out var matchId))
        {
            try
            {
                await SyncAsync(matchId, cancellationToken);
                queue.Done(matchId);
            }
            catch (ProviderThrottledException)
            {
                // El partido vuelve a la cola para cuando termine la pausa.
                queue.Done(matchId);
                queue.Enqueue(matchId);
                throw;
            }
            catch
            {
                queue.Done(matchId);
                throw;
            }
        }
    }

    private async Task<JobOutcome> ScanAsync(int batch, CancellationToken cancellationToken)
    {
        IReadOnlyList<PlannedSheet> due;
        using (var scope = scopeFactory.CreateScope())
        {
            var plan = scope.ServiceProvider.GetRequiredService<ICommandHandler<PlanOfficialSheetSyncCommand, IReadOnlyList<PlannedSheet>>>();
            due = await plan.Handle(new PlanOfficialSheetSyncCommand(batch), cancellationToken);
        }

        if (due.Count == 0) return new JobOutcome("Ninguna acta pendiente", []);
        logger.LogInformation("Actas oficiales: {Count} por descargar o refrescar en segundo plano", due.Count);

        var done = 0;
        var errors = new List<string>();
        foreach (var item in due)
        {
            // Lo que piden los usuarios va siempre por delante de la pasada general.
            await DrainQueueAsync(cancellationToken);
            if (queue.IsQueued(item.MatchId)) continue;
            if (await SyncAsync(item.MatchId, cancellationToken, item.FederationAllowed) is { } error) errors.Add(error);
            done++;
        }

        logger.LogInformation("Actas oficiales: pasada terminada, {Done} partidos consultados", done);
        return new JobOutcome($"{due.Count} actas pendientes, {done} partidos consultados, {done - errors.Count} sin error", errors);
    }

    private async Task<string?> SyncAsync(Guid matchId, CancellationToken cancellationToken, bool federationAllowed = true)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto>>();
        try
        {
            await handler.Handle(new SyncOfficialSheetCommand(matchId, Background: true, FederationAllowed: federationAllowed), cancellationToken);
            return null;
        }
        catch (ProviderThrottledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudo descargar el acta del partido {MatchId}", matchId);
            return $"Partido {matchId}: {ex.Message}";
        }
    }
}
