using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Performances.RecalculateRatings;

namespace TercerTiempo.Api.Extensions;

/// <summary>Tareas que se ejecutan una vez al arrancar, antes de aceptar tráfico.</summary>
internal static class StartupTasks
{
    /// <summary>Calcula la nota de las líneas de acta antiguas (cuando la ponía el cuerpo técnico) o que se quedaron sin ella.</summary>
    public static async Task RecalculateMissingRatingsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecalculateMissingRatingsCommand, int>>();
        await handler.Handle(new RecalculateMissingRatingsCommand(), cancellationToken);
    }
}
