using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Videos.Workers;

// Analiza los vídeos de la cola de uno en uno (la GPU es una), aparte del compresor: un análisis que falla o que no
// puede arrancar (sin uv, sin GPU) nunca frena la compresión. Se despierta cuando el compresor encola un vídeo y, si
// no, mira la cola cada Analysis:PollSeconds (ahí llega lo que se pide desde la API). Al arrancar devuelve a la cola
// lo que se quedó a medias en la parada anterior.
internal sealed class VideoAnalysisWorker(
    IServiceScopeFactory scopes,
    IVideoAnalysisSignal signal,
    IOptions<VideoAnalysisOptions> options,
    ILogger<VideoAnalysisWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(8);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Análisis de vídeos desactivado (Analysis:Enabled): los vídeos no se analizan en este proceso");
            return;
        }

        await Task.Delay(StartDelay, stoppingToken);
        await RecoverAsync(stoppingToken);

        var poll = TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await AnalyzeOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "El analizador de vídeos falló; se vuelve a intentar en {Seconds} s", poll.TotalSeconds);
            }

            await signal.WaitAsync(poll, stoppingToken);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var requeued = await scope.ServiceProvider.GetRequiredService<VideoAnalysisProcessor>().RequeueInterruptedAsync(cancellationToken);
            if (requeued > 0)
                logger.LogInformation("Análisis interrumpidos en la parada anterior: {Requeued} vuelven a la cola", requeued);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo revisar los análisis que quedaron a medias; se sigue con la cola");
        }
    }

    // true si había algo que hacer (lo haya terminado o no).
    private async Task<bool> AnalyzeOneAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<VideoAnalysisProcessor>();

        MatchVideoAnalysis? analysis;
        try
        {
            analysis = await processor.ClaimNextAsync(stoppingToken);
        }
        catch (ConcurrentChangeException)
        {
            // Otro proceso (otra instancia del servicio contra la misma base) se lo quedó antes: al siguiente.
            return true;
        }
        if (analysis is null) return false;

        // Si se elimina el vídeo mientras se analiza, el borrado avisa y aquí se para el analizador.
        using var tracking = signal.Track(analysis.VideoId, out var deleted);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deleted);
        try
        {
            await processor.ProcessAsync(analysis, linked.Token);
        }
        catch (OperationCanceledException) when (deleted.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Vídeo {Video}: eliminado mientras se analizaba", analysis.VideoId);
        }
        return true;
    }
}
