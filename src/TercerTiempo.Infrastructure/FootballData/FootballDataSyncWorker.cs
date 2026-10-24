using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sync;
using TercerTiempo.Infrastructure.Jobs;

namespace TercerTiempo.Infrastructure.FootballData;

internal sealed class FootballDataSyncWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    ISyncProgress progress,
    TimeProvider clock,
    ILogger<FootballDataSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.FootballData);
        try
        {
            var due = await scheduler.FirstDueAsync(job.Name, stoppingToken);
            if (due is not null) logger.LogInformation("Próxima sincronización de datos: {Due}", due);

            while (!stoppingToken.IsCancellationRequested)
            {
                progress.ScheduleNext(due);
                var manual = await scheduler.WaitAsync(job.Name, due, stoppingToken);
                progress.ScheduleNext(null);

                try
                {
                    await scheduler.RunAsync(job.Name, manual, RunOnceAsync, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error sincronizando los datos de fútbol");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncSeasonCommand, SyncResultDto>>();

        var result = await handler.Handle(new SyncSeasonCommand(), cancellationToken);

        logger.LogInformation(
            "Datos de fútbol sincronizados ({Provider}): {Competitions} competiciones, {Teams} equipos, {Matches} partidos, {Roster} fichas de plantilla, {Errors} errores",
            result.Provider, result.Competitions, result.Teams, result.Matches, result.RosterEntries, result.Errors.Count);

        return new JobOutcome(
            $"{result.Competitions} ligas, {result.Teams} equipos, {result.Matches} partidos, {result.Standings} filas de clasificación y {result.RosterEntries} fichas",
            result.Errors);
    }
}
