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

namespace TercerTiempo.Infrastructure.OfficialSheets;

internal sealed class OfficialSheetQueue : IOfficialSheetQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    public bool Enqueue(Guid matchId) => _queued.TryAdd(matchId, 0) && _channel.Writer.TryWrite(matchId);

    public bool IsQueued(Guid matchId) => _queued.ContainsKey(matchId);

    public bool TryDequeue(out Guid matchId) => _channel.Reader.TryRead(out matchId);

    public void Done(Guid matchId) => _queued.TryRemove(matchId, out _);

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _channel.Reader.WaitToReadAsync(cancellationToken);

    public DateTimeOffset? PausedUntil { get; set; }
}

// Descarga las actas públicas en segundo plano y las guarda en la base de datos, para que abrir un partido no espere a
// las webs externas. Atiende primero los partidos que piden los usuarios (cola) y, cada BackgroundScanMinutes desde la
// última pasada guardada (no al arrancar), recorre las temporadas en curso buscando actas que falten o toque refrescar.
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
                await Task.WhenAny(queued, scheduler.WhenTriggered(job.Name));
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
