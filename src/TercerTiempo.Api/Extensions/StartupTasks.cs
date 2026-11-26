using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Performances.RecalculateRatings;
using TercerTiempo.Application.Videos;

namespace TercerTiempo.Api.Extensions;

internal static class StartupTasks
{
    public static void ApplyCrestPolicy(this IServiceProvider services) =>
        TeamCrests.ClubUploadsOnly = services.GetRequiredService<IOptions<CrestOptions>>().Value.ClubUploadsOnly;

    // Si falla, lo vuelve a intentar el próximo arranque; mientras tanto cada acta interna se recalcula al escribirla.
    public static void RecalculateMissingRatingsInBackground(this WebApplication app) =>
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            var stopping = app.Lifetime.ApplicationStopping;
            try
            {
                using var scope = app.Services.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecalculateMissingRatingsCommand, int>>();
                await handler.Handle(new RecalculateMissingRatingsCommand(), stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "No se han podido recalcular las notas pendientes al arrancar");
            }
        }));

    public static async Task NameVideosAfterMatchAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<NameVideosAfterMatchCommand, int>>();
        await handler.Handle(new NameVideosAfterMatchCommand(), cancellationToken);
    }
}
