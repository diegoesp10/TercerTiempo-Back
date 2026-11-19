using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Performances.RecalculateRatings;

public sealed record RecalculateMissingRatingsCommand : ICommand<int>;

internal sealed class RecalculateMissingRatingsHandler(
    IPerformanceStore performances,
    ISquadStore squads,
    IMatchStore matches,
    OfficialSheetRatings officialRatings,
    IUnitOfWork unitOfWork,
    ILogger<RecalculateMissingRatingsHandler> logger) : ICommandHandler<RecalculateMissingRatingsCommand, int>
{
    public async Task<int> Handle(RecalculateMissingRatingsCommand command, CancellationToken cancellationToken)
    {
        var pending = await performances.GetWithoutRatingAsync(cancellationToken);
        var recalculated = await RatingRecalculator.RecalculateAsync(pending, [], matches, cancellationToken, squads);
        // Los goles encajados con cada uno en el campo, por si el acta oficial llegó o cambió con la API parada.
        var fromSheets = await officialRatings.SyncAllAsync(cancellationToken);
        if (recalculated + fromSheets == 0) return 0;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (recalculated > 0) logger.LogInformation("Notas calculadas con los datos para {Count} líneas de acta que no la tenían", recalculated);
        if (fromSheets > 0) logger.LogInformation("Notas recalculadas con los goles encajados en el campo (acta oficial) para {Count} líneas de acta", fromSheets);
        return recalculated + fromSheets;
    }
}
