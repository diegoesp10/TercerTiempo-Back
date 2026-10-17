using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Performances.ListMyPerformances;

namespace TercerTiempo.Repository.Performances;

internal sealed class PerformancesRepository(
    IQueryHandler<ListMyPerformancesQuery, IReadOnlyList<PerformanceWithMatchDto>> listMine) : IPerformancesRepository
{
    public Task<IReadOnlyList<PerformanceWithMatchDto>> GetMineAsync(CancellationToken cancellationToken = default) =>
        listMine.Handle(new ListMyPerformancesQuery(), cancellationToken);
}
