using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Videos.Processing;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Videos.Workers;

// Comprime los vídeos de la cola de uno en uno, en cuanto se sube uno (señal) o, si no, cada PollSeconds. Al arrancar
// devuelve a la cola lo que se quedó a medias en la parada anterior.
internal sealed class VideoProcessingWorker(
    IServiceScopeFactory scopes,
    IVideoProcessingSignal signal,
    IVideoTranscoder transcoder,
    IOptions<VideoProcessingOptions> options,
    ILogger<VideoProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Compresión de vídeos desactivada (VideoProcessing:Enabled): los vídeos se sirven tal cual se suben");
            return;
        }

        // Que la API haya aplicado las migraciones y el proceso esté sirviendo antes de ocupar la CPU.
        await Task.Delay(StartDelay, stoppingToken);

        if (!transcoder.IsAvailable)
        {
            logger.LogWarning("Sin ffmpeg (VideoProcessing:FfmpegPath): los vídeos se quedan en cola, tal cual se subieron, hasta que esté instalado");
            return;
        }

        await RecoverAsync(stoppingToken);

        var poll = TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Mientras haya cola se sigue sin esperar.
                if (await ProcessOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "El compresor de vídeos falló; se vuelve a intentar en {Seconds} s", poll.TotalSeconds);
            }

            await signal.WaitAsync(poll, stoppingToken);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var requeued = await scope.ServiceProvider.GetRequiredService<VideoProcessor>().RequeueInterruptedAsync(cancellationToken);
            var cleaned = await transcoder.CleanUpAsync(cancellationToken);
            if (requeued > 0 || cleaned > 0)
                logger.LogInformation("Compresiones interrumpidas en la parada anterior: {Requeued} vuelven a la cola, {Cleaned} archivos a medias borrados", requeued, cleaned);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo revisar lo que quedó a medias; se sigue con la cola");
        }
    }

    // true si había algo que hacer (lo haya terminado o no).
    private async Task<bool> ProcessOneAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<VideoProcessor>();

        MatchVideo? video;
        try
        {
            video = await processor.ClaimNextAsync(stoppingToken);
        }
        catch (ConcurrentChangeException)
        {
            // Otro proceso (otra instancia del servicio contra la misma base) se lo quedó antes: al siguiente.
            return true;
        }
        if (video is null) return false;

        // Si se elimina el vídeo mientras se comprime, el borrado avisa y aquí se para ffmpeg.
        using var tracking = signal.Track(video.Id, out var deleted);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deleted);
        try
        {
            await processor.ProcessAsync(video, linked.Token);
        }
        catch (OperationCanceledException) when (deleted.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Vídeo {Video}: eliminado mientras se comprimía", video.Id);
        }
        return true;
    }
}
