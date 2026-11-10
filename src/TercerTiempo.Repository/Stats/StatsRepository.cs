using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Stats;
using TercerTiempo.Application.Stats.GetLeagueTable;
using TercerTiempo.Application.Stats.Predictions;

namespace TercerTiempo.Repository.Stats;

internal sealed class StatsRepository(
    IQueryHandler<GetLeagueTableQuery, LeagueTableDto> getLeagueTable,
    IQueryHandler<GetLeaguePredictionQuery, LeaguePredictionDto> getPrediction,
    IQueryHandler<GetLeaguePredictionEvolutionQuery, LeaguePredictionEvolutionDto> getEvolution) : IStatsRepository
{
    public Task<LeagueTableDto> GetLeagueTableAsync(Guid? competitionId, CancellationToken cancellationToken = default) =>
        getLeagueTable.Handle(new GetLeagueTableQuery(competitionId), cancellationToken);

    public Task<LeaguePredictionDto> GetPredictionAsync(Guid competitionId, int? matchday, CancellationToken cancellationToken = default) =>
        getPrediction.Handle(new GetLeaguePredictionQuery(competitionId, matchday), cancellationToken);

    public Task<LeaguePredictionEvolutionDto> GetPredictionEvolutionAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        getEvolution.Handle(new GetLeaguePredictionEvolutionQuery(competitionId), cancellationToken);
}
