using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
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
    IOfficialSheetStore sheets,
    IVideoAnalyzer analyzer,
    IVideoJobLog jobs,
    IOptions<VideoAnalysisOptions> options,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<VideoAnalysisProcessor> logger)
{
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(5);
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    // Cada cuánto se renueva el latido aunque el avance no cambie.
    private static readonly TimeSpan LeaseBeatInterval = TimeSpan.FromSeconds(15);

    // Cada cuánto se mira si el cuerpo técnico ha pausado, reanudado o cancelado: lo que tarda en notarse.
    private static readonly TimeSpan ControlInterval = TimeSpan.FromSeconds(3);

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
        var owner = analysis.WorkerId;

        // La fila la guardan lo que cuenta el analizador y el vigilante (latido y órdenes), que corren a la vez sobre la
        // misma unidad de trabajo: de uno en uno.
        using var gate = new SemaphoreSlim(1, 1);
        using var changed = new CancellationTokenSource();
        using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, changed.Token);
        using var watchStop = CancellationTokenSource.CreateLinkedTokenSource(running.Token);
        var control = new VideoAnalysisControl();
        control.Set(analysis.Status == VideoAnalysisStatus.Paused);
        var row = new RowWriter(analyses, unitOfWork, analysis, owner, gate, changed);
        var watch = WatchAsync(row, control, watchStop.Token);

        try
        {
            logger.LogInformation("Vídeo {Video} ({Label}): analizando{Pitch}{Roster}", video.Id, context.Label,
                context.PitchLengthM is { } length ? $" en un campo de {length:0.#} × {context.PitchWidthM:0.#} m" : "",
                context.Roster is { } roster ? $", con los dorsales del acta oficial ({roster.Teams.Sum(t => t.Players.Count)} jugadores)" : ", sin acta oficial");

            var lastSave = started;
            var percent = 0;
            VideoAnalysisLive? live = null;
            var log = new List<string>();
            var run = await analyzer.AnalyzeAsync(
                new VideoAnalysisRequest(video.Id, video.StorageKey, context.OurTeam, context.RivalTeam, context.PitchLengthM, context.PitchWidthM, context.Roster),
                async (update, token) =>
                {
                    var now = clock.GetUtcNow();
                    // Una línea del registro, un cambio de etapa o entrar y salir de la pausa se guardan enseguida; el
                    // resto (avance, minuto del vídeo…) como mucho cada pocos segundos.
                    var urgent = update.Log is not null
                        || (update.Live is { } next && (next.Stage != live?.Stage || next.Paused != live?.Paused));
                    if (update.Percent is { } p) percent = Math.Max(percent, p);
                    if (update.Live is { } l) live = l;
                    if (update.Log is { } text) log.Add(text);
                    if (!urgent && now - lastSave < ProgressSaveInterval) return;

                    var pending = log.ToList();
                    var snapshot = live;
                    var reached = percent;
                    await row.SaveAsync(a =>
                    {
                        a.ReportLive(reached, snapshot?.Stage, snapshot is null ? null : LiveJson(snapshot), now);
                        foreach (var entry in pending) a.AddLog(entry, now);
                        a.Beat(now);
                    }, token);
                    log.RemoveRange(0, pending.Count);
                    lastSave = now;
                },
                control,
                running.Token);

            await StopWatchAsync(watchStop, watch);
            changed.Token.ThrowIfCancellationRequested();

            // Terminado el análisis, el resultado se guarda aunque el servicio se esté parando (y aunque justo se hubiera
            // pedido una pausa: ya no hay nada que pausar).
            var outcome = new VideoAnalysisOutcome(run.OutputPath, run.AnalyzerVersion, run.Weights, run.ProposalJson, run.Frames,
                run.BallFramesPct, run.CalibratedFramesPct, run.AnalyzedSeconds, run.PitchLengthM, run.PitchWidthM, run.ReadFramesPct);
            var crops = run.Crops.Select(c => new VideoAnalysisCrop(analysis.Id, c.Key, c.Value)).ToList();
            await row.SaveResultAsync(() => analysis.Complete(outcome, clock.GetUtcNow()), crops);

            var minutes = Math.Max(1, (int)Math.Round((clock.GetUtcNow() - started).TotalMinutes - analysis.PausedSeconds / 60.0));
            var read = run.ReadFramesPct is { } pct && pct < MatchVideoAnalysis.LowReadFramesPct
                ? string.Create(Spanish, $" Solo se pudo leer el {pct:0.#} % de los fotogramas del tramo.")
                : "";
            var proposal = VideoProposal.Parse(run.ProposalJson);
            var players = proposal.Identified
                ? $"{proposal.Players.Count} jugadores identificados por el dorsal"
                : $"{run.Crops.Count} jugadores seguidos";
            var summary = string.Create(Spanish,
                $"{context.Label}: {players}, balón a la vista en el {run.BallFramesPct:0.#} % de los fotogramas y campo calibrado en el {run.CalibratedFramesPct:0.#} %, en {minutes} min.{read}");
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
            // Eliminado mientras se analizaba (no hay nada que guardar) o apagado del servicio (vuelve a la cola o, si
            // estaba en pausa, se queda en pausa para empezar de nuevo al reanudarlo).
            if (!await videos.IsDeletedAsync(video.Id, CancellationToken.None) && await TryDiscardAsync(analysis) && analysis.IsWorking)
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
            if (await TryDiscardAsync(analysis) && analysis.IsWorking)
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
            await StopWatchAsync(watchStop, watch);
        }
    }

    private static readonly JsonSerializerOptions LiveJsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    internal static string LiveJson(VideoAnalysisLive live) => JsonSerializer.Serialize(live, LiveJsonOptions);

    internal static VideoAnalysisLive? ReadLive(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<VideoAnalysisLive>(json, LiveJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Stopped(Exception ex, CancellationTokenSource changed, CancellationToken cancellationToken) =>
        ex is ConcurrentChangeException || (ex is OperationCanceledException && changed.IsCancellationRequested && !cancellationToken.IsCancellationRequested);

    // Cada pocos segundos mira cómo está la fila en la base: si el cuerpo técnico la ha pausado o reanudado, se lo dice al
    // analizador; si la ha cancelado (o el vídeo se ha ido, u otro analizador la ha retomado), para todo. Y renueva el
    // latido, también en pausa, para que nadie la dé por abandonada.
    private async Task WatchAsync(RowWriter row, VideoAnalysisControl control, CancellationToken cancellationToken)
    {
        var lastBeat = clock.GetUtcNow();
        try
        {
            while (true)
            {
                await Task.Delay(ControlInterval, clock, cancellationToken);
                try
                {
                    var state = await row.ReadControlAsync(cancellationToken);
                    if (state is null || state.WorkerId != row.Owner || state.Status is not (VideoAnalysisStatus.Running or VideoAnalysisStatus.Paused))
                    {
                        await row.StopAsync();
                        return;
                    }
                    if (control.Paused != (state.Status == VideoAnalysisStatus.Paused))
                        logger.LogInformation("Vídeo {Video}: análisis {Action}", row.VideoId, state.Status == VideoAnalysisStatus.Paused ? "en pausa" : "reanudado");
                    control.Set(state.Status == VideoAnalysisStatus.Paused);

                    var now = clock.GetUtcNow();
                    if (now - lastBeat >= LeaseBeatInterval)
                    {
                        await row.SaveAsync(a => a.Beat(now), cancellationToken);
                        lastBeat = now;
                    }
                }
                catch (ConcurrentChangeException)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Vídeo {Video}: no se pudo mirar ni renovar el análisis; se vuelve a intentar", row.VideoId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Terminó el análisis (o se paró el servicio).
        }
    }

    // Guarda los cambios de la fila de uno en uno. Si al guardar la fila ha cambiado en la base (una pausa o una
    // reanudación pedida desde la app), se relee y se vuelve a aplicar mientras siga siendo de este analizador; si no
    // (cancelado, retomado por otro), se para el análisis.
    private sealed class RowWriter(IMatchVideoAnalysisStore analyses, IUnitOfWork unitOfWork, MatchVideoAnalysis analysis, string? owner, SemaphoreSlim gate, CancellationTokenSource changed)
    {
        private const int MaxRetries = 3;

        public string? Owner => owner;

        public Guid VideoId => analysis.VideoId;

        public async Task SaveAsync(Action<MatchVideoAnalysis> change, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        change(analysis);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        return;
                    }
                    catch (ConcurrentChangeException) when (attempt < MaxRetries)
                    {
                        if (!await analyses.DiscardChangesAsync(analysis, cancellationToken) || !Ours())
                        {
                            await changed.CancelAsync();
                            throw;
                        }
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SaveResultAsync(Action complete, IReadOnlyCollection<VideoAnalysisCrop> crops)
        {
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        complete();
                        await analyses.SaveResultAsync(analysis.Id, crops, CancellationToken.None);
                        return;
                    }
                    catch (ConcurrentChangeException) when (attempt < MaxRetries)
                    {
                        if (!await analyses.DiscardChangesAsync(analysis, CancellationToken.None) || !Ours())
                            throw;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<VideoAnalysisControlState?> ReadControlAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await analyses.GetControlAsync(analysis.Id, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        public Task StopAsync() => changed.CancelAsync();

        private bool Ours() => analysis.IsWorking && analysis.WorkerId == owner;
    }

    private static async Task StopWatchAsync(CancellationTokenSource stop, Task watch)
    {
        await stop.CancelAsync();
        await watch;
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
        if (match is null) return new AnalysisContext($"{video.Title} ({part})", "Nuestro equipo", "Rival", null, null, null);

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
            home?.Profile.HasPitch == true ? home.Profile.PitchWidthM : null,
            Roster(await sheets.GetByMatchAsync(match.Id, cancellationToken), video.Part));
    }

    // Del acta oficial (también la provisional, que ya trae las alineaciones), los que jugaron con dorsal: el analizador
    // solo da por bueno un número que esté aquí y en sus minutos. Cada parte dura la mitad de lo que jugó quien lo jugó
    // todo (90 → 45; en categorías con partes más cortas, menos).
    internal static VideoAnalysisRoster? Roster(OfficialMatchSheet? sheet, VideoPart part)
    {
        if (sheet is null || !sheet.HasLineups) return null;
        var full = sheet.Lines.Where(l => l.Played).Select(l => l.MinutesPlayed).DefaultIfEmpty(0).Max();
        var half = full >= 40 ? (int)Math.Round(full / 2.0) : 45;
        var teams = new[] { MatchSide.Home, MatchSide.Away }
            .Select(side => new VideoAnalysisRosterTeam(
                side,
                side == MatchSide.Home ? sheet.HomeTeamName : sheet.AwayTeamName,
                sheet.LinesOf(side)
                    .Where(l => l.Played && l.ShirtNumber is not null)
                    .GroupBy(l => l.ShirtNumber!.Value)
                    .Select(g => g.First())
                    .OrderBy(l => l.ShirtNumber)
                    .Select(l => new VideoAnalysisRosterPlayer(l.ShirtNumber!.Value, l.FullName, l.Started, l.Started ? null : l.MinuteIn, l.MinuteOut, l.Goals + l.PenaltyGoals))
                    .ToList()))
            .Where(t => t.Players.Count > 0)
            .ToList();
        if (teams.Count == 0) return null;

        // La provisional no trae marcador (y le pueden faltar goles): solo la cerrada dice cuántos hubo.
        if (sheet is not { IsAvailable: true, HomeGoals: { } homeGoals, AwayGoals: { } awayGoals })
            return new VideoAnalysisRoster(part, half, teams);
        var goals = sheet.Events
            .Where(e => e.Type is OfficialEventType.Goal or OfficialEventType.PenaltyGoal or OfficialEventType.OwnGoal)
            .OrderBy(e => e.Order)
            .Select(e => new VideoAnalysisGoal(
                e.Type == OfficialEventType.OwnGoal ? (e.Side == MatchSide.Home ? MatchSide.Away : MatchSide.Home) : e.Side,
                e.Minute,
                e.ExtraMinute ?? 0))
            .ToList();
        return new VideoAnalysisRoster(part, half, teams, new VideoAnalysisScore(homeGoals, awayGoals), goals);
    }

    private sealed record AnalysisContext(string Label, string OurTeam, string RivalTeam, decimal? PitchLengthM, decimal? PitchWidthM, VideoAnalysisRoster? Roster);
}
