using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Jobs;

// Cada compresión y cada análisis quedan en JobRuns, en la base del entorno, como una ejecución más de los trabajos
// video-transcode y video-analysis: así /jobs las enseña con las demás aunque las haga el servicio de vídeo y no la API.
internal sealed class VideoJobLog(IServiceScopeFactory scopes, IHostEnvironment environment, TimeProvider clock, ILogger<VideoJobLog> logger) : IVideoJobLog
{
    public async Task<Guid?> StartedAsync(string job, string label, CancellationToken cancellationToken)
    {
        var run = new JobRun
        {
            Id = Guid.CreateVersion7(),
            Job = job,
            Manual = false,
            Status = JobRunStatus.Running,
            StartedAt = clock.GetUtcNow(),
            ScopeKind = "Video",
            ScopeLabel = label.Length > 200 ? label[..200] : label,
            Environment = environment.EnvironmentName,
        };

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(run);
            await db.SaveChangesAsync(cancellationToken);
            return run.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo apuntar en /jobs el inicio de {Job} de {Label}", job, label);
            return null;
        }
    }

    public async Task FinishedAsync(Guid? runId, bool succeeded, string summary, string? error, CancellationToken cancellationToken)
    {
        if (runId is null) return;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.Set<JobRun>().SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
            if (run is null) return;

            run.Status = succeeded ? JobRunStatus.Succeeded : JobRunStatus.Failed;
            run.FinishedAt = clock.GetUtcNow();
            run.Summary = summary.Length > 1000 ? summary[..1000] : summary;
            run.Errors = string.IsNullOrWhiteSpace(error) ? null : error.ReplaceLineEndings(" ").Trim();
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo apuntar en /jobs el final de la ejecución {Run}", runId);
        }
    }
}
