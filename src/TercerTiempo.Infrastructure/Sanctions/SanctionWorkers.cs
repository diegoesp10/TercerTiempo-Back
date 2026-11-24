using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Sanctions;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Infrastructure.Sanctions;

// Sanciones del comité de competición: de asturfutbol.es a la base de datos reales (solo el entorno con el bloqueo).
internal sealed class CommitteeSanctionsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<CommitteeSanctionsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var realData = RealDataAmbient.Enter();
        var job = scheduler.Get(JobNames.CommitteeSanctions);
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
                    logger.LogError(ex, "Error leyendo las sanciones del comité de competición");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncCommitteeSanctionsCommand, CommitteeSanctionsSynced>>();
        var result = await handler.Handle(new SyncCommitteeSanctionsCommand(), cancellationToken);
        if (result.Checked == 0 && result.Errors.Count == 0)
            return new JobOutcome($"Ningún grupo tocaba ({result.Groups} al día)", [], Quiet: true);

        var summary = $"{result.Checked} de {result.Groups} {(result.Groups == 1 ? "grupo consultado" : "grupos consultados")}";
        var changes = new List<string>();
        if (result.Added > 0) changes.Add(result.Added == 1 ? "1 resolución nueva" : $"{result.Added} resoluciones nuevas");
        if (result.Changed > 0) changes.Add(result.Changed == 1 ? "1 cambiada" : $"{result.Changed} cambiadas");
        if (result.Removed > 0) changes.Add(result.Removed == 1 ? "1 retirada" : $"{result.Removed} retiradas");
        summary += changes.Count > 0 ? $"; {string.Join(", ", changes)}" : "; sin novedades";
        return new JobOutcome(summary, result.Errors);
    }
}

// Avisos de esas sanciones en la campanita del jugador, su cuerpo técnico y su club (no llama a nada de fuera).
internal sealed class SuspensionAlertsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<SuspensionAlertsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.SuspensionAlerts);
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
                    logger.LogError(ex, "Error creando los avisos de sanciones");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncSuspensionAlertsCommand, SuspensionAlertsSynced>>();
        var result = await handler.Handle(new SyncSuspensionAlertsCommand(), cancellationToken);
        if (result.Raised == 0 && result.Resolved == 0)
            return new JobOutcome("Ningún aviso nuevo", [], Quiet: true);

        var parts = new List<string>();
        if (result.Raised > 0) parts.Add(result.Raised == 1 ? "1 sanción avisada" : $"{result.Raised} sanciones avisadas");
        if (result.Resolved > 0) parts.Add(result.Resolved == 1 ? "1 aviso resuelto" : $"{result.Resolved} avisos resueltos");
        return new JobOutcome(string.Join("; ", parts), []);
    }
}
