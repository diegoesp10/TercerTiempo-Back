using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Infrastructure.Jobs;

namespace TercerTiempo.Infrastructure.Alerts;

// Recordatorios de la convocatoria del próximo partido que sigue sin enviar, en la campanita del club y del cuerpo técnico
// (no llama a nada de fuera).
internal sealed class CallUpRemindersWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<CallUpRemindersWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.CallUpReminders);
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
                    logger.LogError(ex, "Error creando los avisos de convocatoria");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncCallUpRemindersCommand, CallUpRemindersSynced>>();
        var result = await handler.Handle(new SyncCallUpRemindersCommand(), cancellationToken);
        if (result.CallUps == 0 && result.Resolved == 0)
            return new JobOutcome("Ningún aviso nuevo", [], Quiet: true);

        var parts = new List<string>();
        if (result.CallUps > 0)
            parts.Add($"{(result.CallUps == 1 ? "1 convocatoria por enviar" : $"{result.CallUps} convocatorias por enviar")} ({(result.Raised == 1 ? "1 aviso" : $"{result.Raised} avisos")})");
        if (result.Resolved > 0) parts.Add(result.Resolved == 1 ? "1 aviso resuelto" : $"{result.Resolved} avisos resueltos");
        return new JobOutcome(string.Join("; ", parts), []);
    }
}
