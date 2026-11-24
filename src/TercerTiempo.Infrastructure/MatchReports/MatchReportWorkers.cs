using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.MatchReports;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.RealData;

namespace TercerTiempo.Infrastructure.MatchReports;

// Crónicas de los partidos: de futbolasturiano.es a la base de datos reales, solo las frases de las lesiones (solo el
// entorno con el bloqueo).
internal sealed class MatchReportsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<MatchReportsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var realData = RealDataAmbient.Enter();
        var job = scheduler.Get(JobNames.MatchReports);
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
                    logger.LogError(ex, "Error leyendo las crónicas de los partidos");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncMatchReportsCommand, MatchReportsSynced>>();
        var result = await handler.Handle(new SyncMatchReportsCommand(), cancellationToken);
        var expired = result.Expired == 0 ? "" : $"; {(result.Expired == 1 ? "1 frase de más de 60 días borrada" : $"{result.Expired} frases de más de 60 días borradas")}";
        if (result.Due == 0)
            return new JobOutcome($"Ninguna crónica que leer{expired}", [], Quiet: true);

        var summary = $"{result.Read} de {result.Due} {(result.Due == 1 ? "partido consultado" : "partidos consultados")}, "
            + $"{result.WithReport} con crónica";
        var changes = new List<string>();
        if (result.Added > 0) changes.Add(result.Added == 1 ? "1 posible lesión" : $"{result.Added} posibles lesiones");
        if (result.Changed > 0) changes.Add(result.Changed == 1 ? "1 cambiada" : $"{result.Changed} cambiadas");
        if (result.Removed > 0) changes.Add(result.Removed == 1 ? "1 retirada" : $"{result.Removed} retiradas");
        summary += changes.Count > 0 ? $"; {string.Join(", ", changes)}" : "; ninguna lesión";
        summary += expired;
        return new JobOutcome(summary, result.Errors, Quiet: result.Added + result.Changed + result.Removed == 0 && result.Errors.Count == 0);
    }
}

// Propuestas de lesión para las plantillas de este entorno, con su aviso al club y al cuerpo técnico (no llama a nada de fuera).
internal sealed class InjuryProposalsWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<InjuryProposalsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.InjuryProposals);
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
                    logger.LogError(ex, "Error creando las propuestas de lesión");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncInjuryProposalsCommand, InjuryProposalsSynced>>();
        var result = await handler.Handle(new SyncInjuryProposalsCommand(), cancellationToken);
        if (result.Created == 0 && result.Refreshed == 0 && result.Expired == 0)
            return new JobOutcome("Ninguna propuesta nueva", [], Quiet: true);

        var parts = new List<string>();
        if (result.Created > 0) parts.Add(result.Created == 1 ? "1 posible lesión avisada" : $"{result.Created} posibles lesiones avisadas");
        if (result.Refreshed > 0) parts.Add(result.Refreshed == 1 ? "1 actualizada" : $"{result.Refreshed} actualizadas");
        if (result.Expired > 0)
            parts.Add(result.Expired == 1
                ? "1 retirada (la crónica ya no lo cuenta o pasaron 60 días sin registrarla)"
                : $"{result.Expired} retiradas (la crónica ya no lo cuenta o pasaron 60 días sin registrarlas)");
        return new JobOutcome(string.Join("; ", parts), [], Quiet: result.Created == 0 && result.Refreshed == 0);
    }
}
