using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos.Analysis;

// Pone en la cola de análisis un vídeo cuyo archivo ya no va a cambiar. Lo llama el compresor al terminar.
public sealed class VideoAnalysisQueue(
    IMatchVideoAnalysisStore analyses,
    IOptions<VideoAnalysisOptions> options,
    IVideoAnalysisSignal signal,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // true si el vídeo ha entrado en la cola (false si el análisis está desactivado o ya tenía uno).
    public async Task<bool> EnqueueAsync(MatchVideo video, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !MatchVideoAnalysis.CanAnalyze(video)) return false;
        if (await analyses.GetByVideoAsync(video.Id, cancellationToken) is not null) return false;

        analyses.Add(MatchVideoAnalysis.Queue(video, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return true;
    }
}

// Analiza los vídeos de la cola de uno en uno (la GPU es una). Si falla, el vídeo y el acta siguen como estaban: el
// análisis queda fallido con el motivo y se puede volver a pedir. Mientras analiza renueva el latido de la fila; si al
// renovarlo la fila ha cambiado (cancelado, vídeo eliminado, otro analizador lo dio por muerto) se para sin guardar nada.
public sealed class VideoAnalysisProcessor(
    IMatchVideoAnalysisStore analyses,
    IMatchVideoStore videos,
    IMatchStore matches,
    ITeamStore teams,
    IVideoAnalyzer analyzer,
    IVideoJobLog jobs,
    IOptions<VideoAnalysisOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<VideoAnalysisProcessor> logger)
{
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(5);
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    // Cada cuánto se renueva el latido aunque el avance no cambie: también es lo que tarda en notarse una cancelación.
    private static readonly TimeSpan LeaseBeatInterval = TimeSpan.FromSeconds(15);

    private int MaxAttempts => Math.Max(1, options.Value.MaxAttempts);

    private TimeSpan Lease => TimeSpan.FromMinutes(Math.Max(2, options.Value.LeaseMinutes));

    public async Task<MatchVideoAnalysis?> ClaimNextAsync(string workerId, CancellationToken cancellationToken)
    {
        var analysis = await analyses.GetNextPendingAsync(MaxAttempts, cancellationToken);
        if (analysis is null) return null;

        analysis.Start(clock.GetUtcNow(), workerId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return analysis;
    }

    // Devuelve a la cola (o da por fallidos, si ya no quedan intentos) los análisis «en marcha» cuyo proceso ya no está:
    // los de latido caducado y los que `abandoned` reconozca (al arrancar, los de este mismo equipo cuyo proceso murió).
    // Los de otro analizador vivo no se tocan.
    public async Task<int> RecoverAbandonedAsync(Func<string?, bool> abandoned, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lost = (await analyses.GetRunningAsync(cancellationToken))
            .Where(a => a.LeaseExpired(now, Lease) || abandoned(a.WorkerId))
            .ToList();
        foreach (var analysis in lost)
        {
            logger.LogWarning("Vídeo {Video}: el analizador {Worker} dejó de dar señales a medias del análisis", analysis.VideoId, analysis.WorkerId ?? "desconocido");
            analysis.Recover(MaxAttempts, now);
        }
        if (lost.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return lost.Count;
    }

    public async Task ProcessAsync(MatchVideoAnalysis analysis, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(analysis.VideoId, cancellationToken);
        if (video is null || video.IsDeleted)
        {
            analysis.Fail("El vídeo se ha eliminado.", clock.GetUtcNow());
            await TrySaveAsync();
            return;
        }

        var context = await ContextAsync(video, cancellationToken);
        var runId = await jobs.StartedAsync(JobNames.VideoAnalysis, context.Label, cancellationToken);
        var started = clock.GetUtcNow();

        // La fila la guardan el avance y el latido, que corren a la vez sobre la misma unidad de trabajo: de uno en uno.
        using var gate = new SemaphoreSlim(1, 1);
        using var changed = new CancellationTokenSource();
        using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, changed.Token);
        using var leaseStop = CancellationTokenSource.CreateLinkedTokenSource(running.Token);
        var lease = KeepLeaseAsync(analysis, gate, changed, leaseStop.Token);

        try
        {
            logger.LogInformation("Vídeo {Video} ({Label}): analizando{Pitch}", video.Id, context.Label,
                context.PitchLengthM is { } length ? $" en un campo de {length:0.#} × {context.PitchWidthM:0.#} m" : "");

            var lastSave = started;
            var run = await analyzer.AnalyzeAsync(
                new VideoAnalysisRequest(video.Id, video.StorageKey, context.OurTeam, context.RivalTeam, context.PitchLengthM, context.PitchWidthM),
                async (percent, token) =>
                {
                    var now = clock.GetUtcNow();
                    if (percent == analysis.Progress || now - lastSave < ProgressSaveInterval) return;
                    await SaveLockedAsync(gate, () =>
                    {
                        analysis.ReportProgress(percent);
                        analysis.Beat(now);
                    }, token);
                    lastSave = now;
                },
                running.Token);

            await StopLeaseAsync(leaseStop, lease);
            changed.Token.ThrowIfCancellationRequested();

            analysis.Complete(
                new VideoAnalysisOutcome(run.OutputPath, run.AnalyzerVersion, run.Weights, run.ProposalJson, run.Frames,
                    run.BallFramesPct, run.CalibratedFramesPct, run.AnalyzedSeconds, run.PitchLengthM, run.PitchWidthM, run.ReadFramesPct),
                clock.GetUtcNow());
            // Terminado el análisis, el resultado se guarda aunque el servicio se esté parando.
            await analyses.SaveResultAsync(
                analysis.Id, run.Crops.Select(c => new VideoAnalysisCrop(analysis.Id, c.Key, c.Value)).ToList(), CancellationToken.None);

            var minutes = Math.Max(1, (int)Math.Round((clock.GetUtcNow() - started).TotalMinutes));
            var read = run.ReadFramesPct is { } pct && pct < MatchVideoAnalysis.LowReadFramesPct
                ? string.Create(Spanish, $" Solo se pudo leer el {pct:0.#} % de los fotogramas del tramo.")
                : "";
            var summary = string.Create(Spanish,
                $"{context.Label}: {run.Crops.Count} jugadores seguidos, balón a la vista en el {run.BallFramesPct:0.#} % de los fotogramas y campo calibrado en el {run.CalibratedFramesPct:0.#} %, en {minutes} min.{read}");
            logger.LogInformation("Vídeo {Video}: {Summary}", video.Id, summary);
            await jobs.FinishedAsync(runId, true, summary, null, CancellationToken.None);
        }
        catch (Exception ex) when (Stopped(ex, changed, cancellationToken))
        {
            // Lo canceló el cuerpo técnico, se eliminó el vídeo o lo retomó otro analizador: la fila ya no es nuestra.
            logger.LogInformation("Vídeo {Video} ({Label}): el análisis se detiene, la fila cambió mientras se analizaba (cancelado, vídeo eliminado u otro analizador)",
                video.Id, context.Label);
            await jobs.FinishedAsync(runId, false, $"{context.Label}: análisis detenido (cancelado o el vídeo cambió mientras se analizaba).", null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Eliminado mientras se analizaba (no hay nada que guardar) o apagado del servicio (vuelve a la cola).
            if (!await videos.IsDeletedAsync(video.Id, CancellationToken.None) && await TryDiscardAsync(analysis)
                && analysis.Status == VideoAnalysisStatus.Running)
            {
                analysis.Requeue();
                await TrySaveAsync();
            }
            await jobs.FinishedAsync(runId, false, $"{context.Label}: análisis interrumpido.", null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // Un VideoAnalysisException es un fallo del propio análisis (sin GPU, vídeo ilegible, analizador colgado) y no
            // se reintenta; lo demás (la base, el disco) es pasajero y vuelve a la cola mientras queden intentos.
            var definitive = ex is VideoAnalysisException;
            if (definitive)
                logger.LogWarning("Vídeo {Video} ({Label}): no se pudo analizar: {Reason}", video.Id, context.Label, ex.Message);
            else
                logger.LogError(ex, "Vídeo {Video} ({Label}): no se pudo analizar", video.Id, context.Label);

            // Lo que hubiera en memoria (un resultado a medio guardar) no cuenta: manda la fila tal como está en la base.
            var retry = false;
            if (await TryDiscardAsync(analysis) && analysis.Status == VideoAnalysisStatus.Running)
            {
                retry = !definitive && analysis.Attempts < MaxAttempts;
                if (retry) analysis.RetryLater(ex.Message);
                else analysis.Fail(ex.Message, clock.GetUtcNow());
                await TrySaveAsync();
            }
            await jobs.FinishedAsync(runId, false,
                retry ? $"{context.Label}: falló (se vuelve a intentar)." : $"{context.Label}: no se pudo analizar.", ex.Message, CancellationToken.None);
        }
        finally
        {
            await StopLeaseAsync(leaseStop, lease);
        }
    }

    private static bool Stopped(Exception ex, CancellationTokenSource changed, CancellationToken cancellationToken) =>
        ex is ConcurrentChangeException || (ex is OperationCanceledException && changed.IsCancellationRequested && !cancellationToken.IsCancellationRequested);

    private async Task KeepLeaseAsync(MatchVideoAnalysis analysis, SemaphoreSlim gate, CancellationTokenSource changed, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(LeaseBeatInterval, clock, cancellationToken);
                try
                {
                    await SaveLockedAsync(gate, () => analysis.Beat(clock.GetUtcNow()), cancellationToken);
                }
                catch (ConcurrentChangeException)
                {
                    await changed.CancelAsync();
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Vídeo {Video}: no se pudo renovar el latido del análisis; se vuelve a intentar", analysis.VideoId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Terminó el análisis (o se paró el servicio).
        }
    }

    private async Task SaveLockedAsync(SemaphoreSlim gate, Action change, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            change();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task StopLeaseAsync(CancellationTokenSource stop, Task lease)
    {
        await stop.CancelAsync();
        await lease;
    }

    private async Task<bool> TryDiscardAsync(MatchVideoAnalysis analysis)
    {
        try
        {
            return await analyses.DiscardChangesAsync(analysis, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Sin base no se puede dejar nada: el latido caducará y otro analizador (o este al volver) lo recogerá.
            logger.LogWarning(ex, "No se pudo releer el estado del análisis del vídeo {Video}", analysis.VideoId);
            return false;
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
            logger.LogWarning(ex, "No se pudo guardar el estado del análisis");
        }
    }

    // El campo es el del equipo local, que es donde se juega: sus medidas las apunta su club en la ficha del equipo.
    private async Task<AnalysisContext> ContextAsync(MatchVideo video, CancellationToken cancellationToken)
    {
        var part = video.Part switch
        {
            VideoPart.FirstHalf => "1ª parte",
            VideoPart.SecondHalf => "2ª parte",
            _ => "partido completo",
        };
        var match = await matches.GetByIdAsync(video.MatchId, cancellationToken);
        if (match is null) return new AnalysisContext($"{video.Title} ({part})", "Nuestro equipo", "Rival", null, null);

        var byId = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
        var home = byId.GetValueOrDefault(match.HomeTeamId);
        var away = byId.GetValueOrDefault(match.AwayTeamId);
        var ours = video.TeamId == match.AwayTeamId ? away : home;
        var rival = video.TeamId == match.AwayTeamId ? home : away;

        return new AnalysisContext(
            $"{home?.Name ?? "Local"} - {away?.Name ?? "Visitante"} ({part})",
            ours?.Name ?? "Nuestro equipo",
            rival?.Name ?? "Rival",
            home?.Profile.HasPitch == true ? home.Profile.PitchLengthM : null,
            home?.Profile.HasPitch == true ? home.Profile.PitchWidthM : null);
    }

    private sealed record AnalysisContext(string Label, string OurTeam, string RivalTeam, decimal? PitchLengthM, decimal? PitchWidthM);
}
