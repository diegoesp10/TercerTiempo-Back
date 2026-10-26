using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Infrastructure.OfficialSheets;

// Cada semana pone en cola (SheetDownloads) las actas de los partidos jugados en las ligas de la federación; las
// descarga OfficialSheetSyncWorker en tandas, igual que las que se piden a mano desde /jobs.
internal sealed class WeeklySheetQueueWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    IOptions<OfficialSheetOptions> options,
    TimeProvider clock,
    ILogger<WeeklySheetQueueWorker> logger) : BackgroundService
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.WeeklySheets);
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
                    logger.LogError(ex, "Error poniendo en cola las actas de la semana");
                }

                due = job.Weekly?.Next(clock.GetUtcNow());
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<QueueRecentSheetsCommand, RecentSheetsQueued>>();
        var result = await handler.Handle(new QueueRecentSheetsCommand(options.Value.WeeklyQueueDays), cancellationToken);

        var days = $"del {result.From.ToString("d MMM", Spanish).TrimEnd('.')} al {result.To.ToString("d MMM", Spanish).TrimEnd('.')}";
        var summary = result.Queued switch
        {
            0 => $"Nada nuevo que poner en cola {days} en {result.Leagues} ligas: ya estaban guardadas o en cola",
            1 => $"1 acta en cola ({days}, {result.Leagues} ligas)",
            _ => $"{result.Queued} actas en cola ({days}, {result.Leagues} ligas)",
        };
        return new JobOutcome(summary, []);
    }
}
