using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.OfficialSheets;
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

// Descarga las actas oficiales de la federación en segundo plano y las guarda en la base de datos, para que abrir un
// partido no espere a la web de la federación. Atiende primero los partidos que piden los usuarios (cola) y, cada
// cierto tiempo, recorre las temporadas en curso buscando actas que falten o toque refrescar.
internal sealed class OfficialSheetSyncWorker(
    IServiceScopeFactory scopeFactory,
    OfficialSheetQueue queue,
    IOptions<OfficialSheetOptions> options,
    TimeProvider clock,
    ILogger<OfficialSheetSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.BackgroundScanMinutes));
        var nextScan = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Max(0, settings.BackgroundStartDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainQueueAsync(stoppingToken);

                if (clock.GetUtcNow() >= nextScan)
                {
                    await ScanAsync(settings.BackgroundBatch, stoppingToken);
                    nextScan = clock.GetUtcNow() + interval;
                }

                var wait = nextScan - clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(wait);
                    try { await queue.WaitAsync(timeout.Token); }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ProviderThrottledException ex)
            {
                logger.LogWarning("La federación está limitando las peticiones ({Message}); pausa de 5 minutos", ex.Message);
                queue.PausedUntil = clock.GetUtcNow() + TimeSpan.FromMinutes(5);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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

    private async Task ScanAsync(int batch, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due;
        using (var scope = scopeFactory.CreateScope())
        {
            var plan = scope.ServiceProvider.GetRequiredService<ICommandHandler<PlanOfficialSheetSyncCommand, IReadOnlyList<Guid>>>();
            due = await plan.Handle(new PlanOfficialSheetSyncCommand(batch), cancellationToken);
        }

        if (due.Count == 0) return;
        logger.LogInformation("Actas oficiales: {Count} por descargar o refrescar en segundo plano", due.Count);

        var done = 0;
        foreach (var matchId in due)
        {
            // Lo que piden los usuarios va siempre por delante de la pasada general.
            await DrainQueueAsync(cancellationToken);
            if (queue.IsQueued(matchId)) continue;
            await SyncAsync(matchId, cancellationToken);
            done++;
        }

        logger.LogInformation("Actas oficiales: pasada terminada, {Done} partidos consultados", done);
    }

    private async Task SyncAsync(Guid matchId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncOfficialSheetCommand, OfficialSheetDto>>();
        try
        {
            await handler.Handle(new SyncOfficialSheetCommand(matchId, Background: true), cancellationToken);
        }
        catch (ProviderThrottledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudo descargar el acta del partido {MatchId}", matchId);
        }
    }
}
