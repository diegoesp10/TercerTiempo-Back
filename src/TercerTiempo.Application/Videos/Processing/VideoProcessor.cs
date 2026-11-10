using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos.Processing;

// Comprime los vídeos de la cola de uno en uno. Mientras uno se comprime se sigue sirviendo el original; al acabar se
// sustituye por el comprimido y se borra el original. Si falla, el original se queda y el vídeo sigue viéndose.
public sealed class VideoProcessor(
    IMatchVideoStore videos,
    IMatchStore matches,
    ITeamStore teams,
    IVideoStorage storage,
    IVideoTranscoder transcoder,
    IVideoJobLog jobs,
    Analysis.VideoAnalysisQueue analysisQueue,
    IOptions<VideoProcessingOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<VideoProcessor> logger)
{
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(5);

    // El siguiente vídeo pendiente, ya marcado como en proceso; null si no hay ninguno.
    public async Task<MatchVideo?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var video = await videos.GetNextPendingAsync(options.Value.MaxAttempts, cancellationToken);
        if (video is null) return null;

        video.StartProcessing(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return video;
    }

    // Lo que se quedó a medias cuando el servicio se paró vuelve a la cola.
    public async Task<int> RequeueInterruptedAsync(CancellationToken cancellationToken)
    {
        var interrupted = await videos.GetProcessingAsync(cancellationToken);
        foreach (var video in interrupted) video.Requeue();
        if (interrupted.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return interrupted.Count;
    }

    public async Task ProcessAsync(MatchVideo video, CancellationToken cancellationToken)
    {
        var label = await LabelAsync(video, cancellationToken);
        var runId = await jobs.StartedAsync(JobNames.VideoTranscode, label, cancellationToken);
        var started = clock.GetUtcNow();
        var originalKey = video.StorageKey;
        var originalSize = video.SizeBytes;
        string? newKey = null;

        try
        {
            var probe = await transcoder.ProbeAsync(video.StorageKey, cancellationToken);
            var decision = TranscodePlan.Decide(probe, options.Value);

            if (decision is TranscodeDecision.Keep keep)
            {
                video.KeepOriginal(Seconds(probe.DurationSeconds), probe.Height, clock.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Vídeo {Video} ({Label}): se deja tal cual, {Reason}", video.Id, label, keep.Reason);
                await jobs.FinishedAsync(runId, true, $"{label}: se deja tal cual, {keep.Reason}.", null, CancellationToken.None);
                await QueueAnalysisAsync(video);
                return;
            }

            var profile = ((TranscodeDecision.Transcode)decision).Profile;
            logger.LogInformation("Vídeo {Video} ({Label}): comprimiendo {Size} GB, {Height}p a {Bitrate} kbps, CRF {Crf} preset {Preset}",
                video.Id, label, Gb(originalSize), probe.Height, probe.BitrateBps / 1000, profile.Crf, profile.Preset);

            var lastSave = started;
            var result = await transcoder.TranscodeAsync(video.StorageKey, profile, async (percent, token) =>
            {
                var now = clock.GetUtcNow();
                if (percent == video.ProcessingProgress || now - lastSave < ProgressSaveInterval) return;
                video.ReportProgress(percent);
                await unitOfWork.SaveChangesAsync(token);
                lastSave = now;
            }, cancellationToken);
            newKey = result.StorageKey;

            video.Compressed(result.StorageKey, result.SizeBytes, "video/mp4", Path.ChangeExtension(video.FileName, ".mp4"),
                Seconds(result.DurationSeconds), result.Height, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            newKey = null;

            await storage.DeleteAsync(originalKey, CancellationToken.None);

            var minutes = Math.Max(1, (int)Math.Round((clock.GetUtcNow() - started).TotalMinutes));
            var summary = $"{label}: de {Gb(originalSize)} GB a {Gb(result.SizeBytes)} GB ({result.Height}p) en {minutes} min.";
            logger.LogInformation("Vídeo {Video}: {Summary}", video.Id, summary);
            await jobs.FinishedAsync(runId, true, summary, null, CancellationToken.None);
            await QueueAnalysisAsync(video);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DiscardAsync(newKey);
            // Eliminado mientras se comprimía (no hay nada que guardar) o apagado del servicio (vuelve a la cola).
            if (!await videos.IsDeletedAsync(video.Id, CancellationToken.None))
            {
                video.Requeue();
                await TrySaveAsync();
            }
            await jobs.FinishedAsync(runId, false, $"{label}: compresión interrumpida.", null, CancellationToken.None);
            throw;
        }
        catch (ConcurrentChangeException)
        {
            // Otro proceso tocó la fila (lo normal: se eliminó el vídeo): lo que haya en disco de más se quita y no se
            // sobreescribe nada.
            await DiscardAsync(newKey);
            logger.LogInformation("Vídeo {Video} ({Label}): cambió mientras se comprimía, se descarta el resultado", video.Id, label);
            await jobs.FinishedAsync(runId, false, $"{label}: el vídeo cambió mientras se comprimía.", null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await DiscardAsync(newKey);
            logger.LogError(ex, "Vídeo {Video} ({Label}): no se pudo comprimir", video.Id, label);
            video.FailProcessing(ex.Message, clock.GetUtcNow());
            await TrySaveAsync();
            await jobs.FinishedAsync(runId, false, $"{label}: no se pudo comprimir; se sirve el original.", ex.Message, CancellationToken.None);
        }
    }

    // El vídeo ya está listo: pasa a la cola de análisis. Si eso falla, la compresión no se da por mala.
    private async Task QueueAnalysisAsync(MatchVideo video)
    {
        try
        {
            if (await analysisQueue.EnqueueAsync(video, CancellationToken.None))
                logger.LogInformation("Vídeo {Video}: en cola para analizarse", video.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vídeo {Video}: no se pudo poner en la cola de análisis", video.Id);
        }
    }

    private async Task DiscardAsync(string? key)
    {
        if (key is null) return;
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo borrar el archivo descartado {Key}", key);
        }
    }

    private async Task TrySaveAsync()
    {
        try
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo guardar el estado del vídeo");
        }
    }

    private async Task<string> LabelAsync(MatchVideo video, CancellationToken cancellationToken)
    {
        var part = video.Part switch
        {
            VideoPart.FirstHalf => "1ª parte",
            VideoPart.SecondHalf => "2ª parte",
            _ => "partido completo",
        };
        var match = await matches.GetByIdAsync(video.MatchId, cancellationToken);
        if (match is null) return $"{video.Title} ({part})";

        var names = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        return $"{names.GetValueOrDefault(match.HomeTeamId, "Local")} - {names.GetValueOrDefault(match.AwayTeamId, "Visitante")} ({part})";
    }

    private static int Seconds(double seconds) => (int)Math.Round(seconds);

    private static string Gb(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
}
