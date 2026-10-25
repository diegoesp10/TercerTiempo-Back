using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Competitions;
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
                    var scope = (manual ? scheduler.TakeParameters(job.Name) as SyncScope : null) ?? await ScheduledScopeAsync(stoppingToken);
                    await scheduler.RunAsync(
                        job.Name, manual, ct => RunOnceAsync(scope, ct), stoppingToken, new JobScope(scope.Kind.ToString(), scope.Describe()));
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

    private async Task<SyncScope> ScheduledScopeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var now = clock.GetUtcNow();
        var settings = await scope.ServiceProvider.GetRequiredService<IFootballSyncSettingsStore>().GetAsync(cancellationToken)
            ?? FootballSyncSettings.CreateDefault(now);
        var lastFull = await scope.ServiceProvider.GetRequiredService<IJobsMonitor>().LastFullFootballSyncAsync(cancellationToken);
        return FootballSyncPlanner.Scheduled(settings, lastFull, now);
    }

    private async Task<JobOutcome> RunOnceAsync(SyncScope syncScope, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncSeasonCommand, SyncResultDto>>();

        var result = await handler.Handle(new SyncSeasonCommand(syncScope), cancellationToken);
        scope.ServiceProvider.GetRequiredService<TercerTiempo.Application.Search.SearchIndexHolder>().Invalidate();

        logger.LogInformation(
            "Datos de fútbol sincronizados ({Provider}): {Competitions} competiciones, {Teams} equipos, {Matches} partidos, {Roster} fichas de plantilla, {Errors} errores",
            result.Provider, result.Competitions, result.Teams, result.Matches, result.RosterEntries, result.Errors.Count);

        List<string> parts = [$"{result.Competitions} ligas"];
        if (result.Teams > 0) parts.Add($"{result.Teams} equipos");
        parts.Add($"{result.Matches} partidos");
        parts.Add($"{result.Standings} filas de clasificación");
        if (syncScope.IncludeRosters) parts.Add($"{result.RosterEntries} fichas");
        var summary = string.Join(", ", parts[..^1]) + " y " + parts[^1];

        // Las actas van después de los partidos: así entran también los que acaba de descubrir esta pasada.
        if (syncScope.IncludeSheets)
        {
            var queued = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestSheetDownloadsCommand, int>>()
                .Handle(new RequestSheetDownloadsCommand(syncScope), cancellationToken);
            summary += queued == 0 ? ". Ninguna acta pendiente" : $". {queued} actas en cola para la federación";
        }

        return new JobOutcome(summary, result.Errors);
    }
}
