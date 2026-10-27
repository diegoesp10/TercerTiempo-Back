using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Infrastructure.Jobs;

namespace TercerTiempo.Infrastructure.FootballData;

// Plazas de la clasificación de cada liga (ascenso, promoción, descenso) desde la web del proveedor, una vez al día.
internal sealed class LeagueZonesWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<LeagueZonesWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.LeagueZones);
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
                    logger.LogError(ex, "Error leyendo las plazas de las ligas");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncLeagueZonesCommand, LeagueZonesSynced>>();
        var result = await handler.Handle(new SyncLeagueZonesCommand(), cancellationToken);
        var summary = $"{result.Updated} de {result.Leagues} {(result.Leagues == 1 ? "liga" : "ligas")} con plazas nuevas o cambiadas" +
            (result.Failed > 0 ? $"; {result.Failed} sin poder leer" : string.Empty);
        return new JobOutcome(summary, []);
    }
}
