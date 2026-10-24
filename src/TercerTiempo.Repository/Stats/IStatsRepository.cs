using TercerTiempo.Application.Stats;

namespace TercerTiempo.Repository.Stats;

public interface IStatsRepository
{
    Task<LeagueTableDto> GetLeagueTableAsync(Guid? competitionId, CancellationToken cancellationToken = default);
}
