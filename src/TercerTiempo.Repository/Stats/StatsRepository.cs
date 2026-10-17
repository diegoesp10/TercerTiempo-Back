using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Stats;
using TercerTiempo.Application.Stats.GetLeagueTable;

namespace TercerTiempo.Repository.Stats;

internal sealed class StatsRepository(IQueryHandler<GetLeagueTableQuery, LeagueTableDto> getLeagueTable) : IStatsRepository
{
    public Task<LeagueTableDto> GetLeagueTableAsync(Guid? competitionId, CancellationToken cancellationToken = default) =>
        getLeagueTable.Handle(new GetLeagueTableQuery(competitionId), cancellationToken);
}
