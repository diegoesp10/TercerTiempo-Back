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
    IUnitOfWork unitOfWork,
    ILogger<RecalculateMissingRatingsHandler> logger) : ICommandHandler<RecalculateMissingRatingsCommand, int>
{
    public async Task<int> Handle(RecalculateMissingRatingsCommand command, CancellationToken cancellationToken)
    {
        var pending = await performances.GetWithoutRatingAsync(cancellationToken);
        if (pending.Count == 0) return 0;

        var recalculated = await RatingRecalculator.RecalculateAsync(pending, [], matches, cancellationToken, squads);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Notas calculadas con los datos para {Count} líneas de acta que no la tenían", recalculated);
        return recalculated;
    }
}
