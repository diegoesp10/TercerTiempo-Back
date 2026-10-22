using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Performances.RecalculateRatings;

namespace TercerTiempo.Api.Extensions;

internal static class StartupTasks
{
    public static async Task RecalculateMissingRatingsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecalculateMissingRatingsCommand, int>>();
        await handler.Handle(new RecalculateMissingRatingsCommand(), cancellationToken);
    }
}
