using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Stats;
using TercerTiempo.Application.Stats.GetLeagueTable;
using TercerTiempo.Application.Stats.GetPlayerDashboard;

namespace TercerTiempo.Repository.Stats;

internal sealed class StatsRepository(
    IQueryHandler<GetPlayerDashboardQuery, PlayerDashboardDto> getDashboard,
    IQueryHandler<GetLeagueTableQuery, LeagueTableDto> getLeagueTable) : IStatsRepository
{
    public Task<PlayerDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        getDashboard.Handle(new GetPlayerDashboardQuery(), cancellationToken);

    public Task<LeagueTableDto> GetLeagueTableAsync(CancellationToken cancellationToken = default) =>
        getLeagueTable.Handle(new GetLeagueTableQuery(), cancellationToken);
}
