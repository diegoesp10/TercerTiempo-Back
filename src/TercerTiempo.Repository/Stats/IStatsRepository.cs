using TercerTiempo.Application.Stats;
using TercerTiempo.Application.Stats.Predictions;

namespace TercerTiempo.Repository.Stats;

public interface IStatsRepository
{
    Task<LeagueTableDto> GetLeagueTableAsync(Guid? competitionId, CancellationToken cancellationToken = default);
    Task<LeaguePredictionDto> GetPredictionAsync(Guid competitionId, int? matchday, CancellationToken cancellationToken = default);
    Task<LeaguePredictionEvolutionDto> GetPredictionEvolutionAsync(Guid competitionId, CancellationToken cancellationToken = default);
}
