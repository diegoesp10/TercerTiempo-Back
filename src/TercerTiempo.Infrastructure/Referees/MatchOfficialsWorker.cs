using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Referees;
using TercerTiempo.Infrastructure.Jobs;

namespace TercerTiempo.Infrastructure.Referees;

// Equipos arbitrales de los partidos: de las actas guardadas (sin peticiones) y de futbolasturiano.es.
internal sealed class MatchOfficialsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<MatchOfficialsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.MatchOfficials);
        if (!job.Enabled) return;
        try
        {
            var due = await scheduler.FirstDueAsync(job.Name, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var manual = await scheduler.WaitAsync(job.Name, due, stoppingToken);
                try
                {
                    await scheduler.RunAsync(job.Name, manual, RunOnceAsync, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error buscando los equipos arbitrales de los partidos");
                }

                due = job.Interval is { } interval ? clock.GetUtcNow() + interval : null;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado ordenado.
        }
    }

    private async Task<JobOutcome> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncMatchOfficialsCommand, MatchOfficialsSynced>>();
        var result = await handler.Handle(new SyncMatchOfficialsCommand(), cancellationToken);
        var summary = $"{result.FromSheets} {(result.FromSheets == 1 ? "partido" : "partidos")} desde las actas guardadas; " +
            $"{result.FromProvider} de {result.Looked} {(result.Looked == 1 ? "página consultada" : "páginas consultadas")} en futbolasturiano.es";
        return new JobOutcome(summary, []);
    }
}
