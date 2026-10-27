using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Alerts;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Infrastructure.Jobs;

namespace TercerTiempo.Infrastructure.Alerts;

// Avisos de caducidad de las licencias en la campanita de cada club (no llama a nada de fuera).
internal sealed class LicenseAlertsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<LicenseAlertsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.LicenseAlerts);
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
                    logger.LogError(ex, "Error creando los avisos de caducidad de las licencias");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncLicenseAlertsCommand, LicenseAlertsSynced>>();
        var result = await handler.Handle(new SyncLicenseAlertsCommand(), cancellationToken);
        var summary = result.Raised == 0 ? "Ningún aviso nuevo" : $"{result.Raised} {(result.Raised == 1 ? "aviso nuevo" : "avisos nuevos")}";
        if (result.Resolved > 0) summary += $"; {result.Resolved} {(result.Resolved == 1 ? "resuelto" : "resueltos")}";
        return new JobOutcome(summary, []);
    }
}
