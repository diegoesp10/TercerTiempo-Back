using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Infrastructure.Jobs;

namespace TercerTiempo.Infrastructure.Squads;

// Las lesiones que seguían abiertas al cambiar de temporada, a la plantilla nueva (no llama a nada de fuera). Al abrir la
// plantilla o al entrarle jugadores ya se hace; esto recoge lo que llega después.
internal sealed class InjuryCarryOverWorker(
    IServiceScopeFactory scopeFactory,
    JobScheduler scheduler,
    TimeProvider clock,
    ILogger<InjuryCarryOverWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = scheduler.Get(JobNames.InjuryCarryOver);
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
                    logger.LogError(ex, "Error pasando las lesiones abiertas a la temporada nueva");
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
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CarryOverInjuriesCommand, InjuryCarryOverResult>>();
        var result = await handler.Handle(new CarryOverInjuriesCommand(), cancellationToken);
        if (!result.Changed) return new JobOutcome("Ninguna lesión que pasar", [], Quiet: true);

        var parts = new List<string>();
        if (result.Carried > 0) parts.Add(result.Carried == 1 ? "1 lesión pasada a la temporada nueva" : $"{result.Carried} lesiones pasadas a la temporada nueva");
        if (result.Ended > 0) parts.Add(result.Ended == 1 ? "1 que allí ya llevaban aparte" : $"{result.Ended} que allí ya llevaban aparte");
        return new JobOutcome(string.Join("; ", parts), []);
    }
}
