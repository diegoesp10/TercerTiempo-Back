using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Sync;

namespace TercerTiempo.Infrastructure.FootballData;

/// <summary>Ejecuta la sincronización con el proveedor al arrancar y después cada N horas.</summary>
internal sealed class FootballDataSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FootballDataOptions> options,
    ILogger<FootballDataSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (settings.SyncOnStartup)
            await RunOnceAsync(stoppingToken);

        if (settings.SyncIntervalHours <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.SyncIntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncSeasonCommand, SyncResultDto>>();

            var result = await handler.Handle(new SyncSeasonCommand(), cancellationToken);

            logger.LogInformation(
                "Datos de fútbol sincronizados ({Provider}): {Competition} {Season} - {Teams} equipos, {Matches} partidos",
                result.Provider, result.Competition, result.Season, result.Teams, result.Matches);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Apagado ordenado.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sincronizando los datos de fútbol");
        }
    }
}
