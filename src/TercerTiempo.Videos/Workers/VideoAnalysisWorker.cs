using System.Diagnostics;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Videos.Analysis;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Videos.Workers;

// Analiza los vídeos de la cola de uno en uno (la GPU es una), aparte del compresor: un análisis que falla o que no
// puede arrancar (sin uv, sin GPU) nunca frena la compresión. Solo corre con Analysis:RunWorker (en Docker no: allí lo
// hace Start-Analyzer.ps1 desde Windows). Se despierta cuando el compresor encola un vídeo y, si no, mira la cola cada
// Analysis:PollSeconds (ahí llega lo que se pide desde la API). Mientras corre deja un latido en la base cada medio
// minuto (la app avisa si no hay ningún analizador encendido) y en cada vuelta devuelve a la cola lo que otro analizador
// dejó a medias al morir. Al arrancar, además, lo que dejó a medias un proceso anterior de este mismo equipo.
internal sealed class VideoAnalysisWorker(
    IServiceScopeFactory scopes,
    IVideoAnalysisSignal signal,
    IOptions<VideoAnalysisOptions> options,
    TimeProvider clock,
    ILogger<VideoAnalysisWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(8);

    // Equipo y proceso: con él se sabe de quién es cada análisis en marcha y si su proceso sigue vivo.
    private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunWorker)
        {
            logger.LogInformation("Este proceso no analiza vídeos (Analysis:RunWorker = false){Queue}",
                options.Value.Enabled ? ": los encola y los atiende el analizador que esté en marcha (Start-Analyzer.ps1)" : "");
            return;
        }

        await Task.Delay(StartDelay, stoppingToken);
        logger.LogInformation("Analizador de vídeo {Worker} en marcha: atiende la cola de análisis", WorkerId);

        var started = clock.GetUtcNow();
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = HeartbeatAsync(started, heartbeatStop.Token);
        try
        {
            await RecoverAsync(IsDeadProcessOfThisMachine, stoppingToken);

            var poll = TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RecoverAsync(_ => false, stoppingToken);
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
        finally
        {
            await heartbeatStop.CancelAsync();
            await heartbeat;
            await ForgetHeartbeatAsync();
        }
    }

    private async Task HeartbeatAsync(DateTimeOffset started, CancellationToken cancellationToken)
    {
        var failing = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IVideoAnalyzerHeartbeatStore>().BeatAsync(WorkerId, started, clock.GetUtcNow(), cancellationToken);
                failing = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Una vez por racha: sin base no hay latido, pero tampoco hace falta repetirlo cada medio minuto.
                if (!failing) logger.LogWarning(ex, "No se pudo dejar el latido del analizador de vídeo en la base");
                failing = true;
            }

            try
            {
                await Task.Delay(VideoAnalyzerHeartbeat.Interval, clock, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Al pararse con orden se despide: la app deja de contar con él enseguida, sin esperar a que caduque el latido.
    private async Task ForgetHeartbeatAsync()
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await scope.ServiceProvider.GetRequiredService<IVideoAnalyzerHeartbeatStore>().RemoveAsync(WorkerId, timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo borrar el latido del analizador al parar");
        }
    }

    private async Task RecoverAsync(Func<string?, bool> abandoned, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var recovered = await scope.ServiceProvider.GetRequiredService<VideoAnalysisProcessor>().RecoverAbandonedAsync(abandoned, cancellationToken);
            if (recovered > 0)
                logger.LogInformation("Análisis que un analizador dejó a medias al pararse: {Recovered} vuelven a la cola (o fallan, si no quedan intentos)", recovered);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo revisar los análisis que quedaron a medias; se sigue con la cola");
        }
    }

    // Un análisis «en marcha» de este mismo equipo cuyo proceso ya no existe (o es otro programa con el mismo número)
    // se quedó a medias: no hace falta esperar a que caduque su latido.
    private static bool IsDeadProcessOfThisMachine(string? workerId)
    {
        if (workerId?.Split(':') is not [var machine, var pidText]
            || !string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(pidText, out var pid))
            return false;
        if (pid == Environment.ProcessId) return true;

        try
        {
            using var process = Process.GetProcessById(pid);
            return !(process.ProcessName.StartsWith("TercerTiempo.Videos", StringComparison.OrdinalIgnoreCase)
                || process.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
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
            analysis = await processor.ClaimNextAsync(WorkerId, stoppingToken);
        }
        catch (ConcurrentChangeException)
        {
            // Otro analizador contra la misma base se lo quedó antes: al siguiente.
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
