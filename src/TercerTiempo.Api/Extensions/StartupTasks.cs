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

    public static async Task RecalculateMissingRatingsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecalculateMissingRatingsCommand, int>>();
        await handler.Handle(new RecalculateMissingRatingsCommand(), cancellationToken);
    }

    public static async Task NameVideosAfterMatchAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<NameVideosAfterMatchCommand, int>>();
        await handler.Handle(new NameVideosAfterMatchCommand(), cancellationToken);
    }
}
