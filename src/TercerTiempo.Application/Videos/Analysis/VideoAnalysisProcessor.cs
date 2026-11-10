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
// análisis queda fallido con el motivo y se puede volver a pedir.
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

    public async Task<MatchVideoAnalysis?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var analysis = await analyses.GetNextPendingAsync(Math.Max(1, options.Value.MaxAttempts), cancellationToken);
        if (analysis is null) return null;

        analysis.Start(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return analysis;
    }

    public async Task<int> RequeueInterruptedAsync(CancellationToken cancellationToken)
    {
        var interrupted = await analyses.GetRunningAsync(cancellationToken);
        foreach (var analysis in interrupted) analysis.Requeue();
        if (interrupted.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return interrupted.Count;
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
                    analysis.ReportProgress(percent);
                    await unitOfWork.SaveChangesAsync(token);
                    lastSave = now;
                },
                cancellationToken);

            analysis.Complete(
                new VideoAnalysisOutcome(run.OutputPath, run.AnalyzerVersion, run.Weights, run.ProposalJson, run.Frames,
                    run.BallFramesPct, run.CalibratedFramesPct, run.AnalyzedSeconds, run.PitchLengthM, run.PitchWidthM),
                clock.GetUtcNow());
            await analyses.ReplaceCropsAsync(
                analysis.Id, run.Crops.Select(c => new VideoAnalysisCrop(analysis.Id, c.Key, c.Value)).ToList(), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var minutes = Math.Max(1, (int)Math.Round((clock.GetUtcNow() - started).TotalMinutes));
            var summary = string.Create(Spanish,
                $"{context.Label}: {run.Crops.Count} jugadores seguidos, balón a la vista en el {run.BallFramesPct:0.#} % de los fotogramas y campo calibrado en el {run.CalibratedFramesPct:0.#} %, en {minutes} min.");
            logger.LogInformation("Vídeo {Video}: {Summary}", video.Id, summary);
            await jobs.FinishedAsync(runId, true, summary, null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Eliminado mientras se analizaba (no hay nada que guardar) o apagado del servicio (vuelve a la cola).
            if (!await videos.IsDeletedAsync(video.Id, CancellationToken.None))
            {
                analysis.Requeue();
                await TrySaveAsync();
            }
            await jobs.FinishedAsync(runId, false, $"{context.Label}: análisis interrumpido.", null, CancellationToken.None);
            throw;
        }
        catch (ConcurrentChangeException)
        {
            logger.LogInformation("Vídeo {Video} ({Label}): cambió mientras se analizaba, se descarta el resultado", video.Id, context.Label);
            await jobs.FinishedAsync(runId, false, $"{context.Label}: el vídeo cambió mientras se analizaba.", null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (ex is VideoAnalysisException)
                logger.LogWarning("Vídeo {Video} ({Label}): no se pudo analizar: {Reason}", video.Id, context.Label, ex.Message);
            else
                logger.LogError(ex, "Vídeo {Video} ({Label}): no se pudo analizar", video.Id, context.Label);

            if (analysis.Status == VideoAnalysisStatus.Running) analysis.Fail(ex.Message, clock.GetUtcNow());
            await TrySaveAsync();
            await jobs.FinishedAsync(runId, false, $"{context.Label}: no se pudo analizar.", ex.Message, CancellationToken.None);
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
