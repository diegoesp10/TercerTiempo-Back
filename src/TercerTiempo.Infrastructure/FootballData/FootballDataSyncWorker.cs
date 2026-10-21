using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams.SyncProfiles;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Infrastructure.FootballData;

/// <summary>Ejecuta la sincronización con el proveedor al arrancar y después cada N horas.</summary>
internal sealed class FootballDataSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FootballDataOptions> options,
    IOptions<OfficialSheetOptions> officialSheets,
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
                "Datos de fútbol sincronizados ({Provider}): {Competitions} competiciones, {Teams} equipos, {Matches} partidos, {Roster} fichas de plantilla, {Errors} errores",
                result.Provider, result.Competitions, result.Teams, result.Matches, result.RosterEntries, result.Errors.Count);

            // Después de los datos, las fichas y escudos de la federación (solo los que toca refrescar).
            if (!officialSheets.Value.Enabled) return;

            var profiles = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncTeamProfilesCommand, TeamProfileSyncResultDto>>();
            var profileResult = await profiles.Handle(new SyncTeamProfilesCommand(), cancellationToken);

            logger.LogInformation(
                "Fichas de equipos sincronizadas ({Provider}): {Linked} equipos localizados, {Updated} fichas, {Crests} escudos, {Missing} sin ficha, {Errors} errores",
                profileResult.Provider, profileResult.TeamsLinked, profileResult.ProfilesUpdated, profileResult.CrestsDownloaded, profileResult.TeamsWithoutProfile, profileResult.Errors.Count);
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
