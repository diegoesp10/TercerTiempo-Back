using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Competitions.ListCompetitions;
using TercerTiempo.Application.Competitions.Matchdays;

namespace TercerTiempo.Repository.Competitions;

internal sealed class CompetitionsRepository(
    IQueryHandler<ListCompetitionsQuery, IReadOnlyList<CompetitionDto>> listCompetitions,
    IQueryHandler<GetCompetitionMatchdayQuery, CompetitionMatchdayDto> getMatchday) : ICompetitionsRepository
{
    public Task<IReadOnlyList<CompetitionDto>> ListAsync(CancellationToken cancellationToken = default) =>
        listCompetitions.Handle(new ListCompetitionsQuery(), cancellationToken);

    public Task<CompetitionMatchdayDto> GetMatchdayAsync(Guid competitionId, int? matchday, CancellationToken cancellationToken = default) =>
        getMatchday.Handle(new GetCompetitionMatchdayQuery(competitionId, matchday), cancellationToken);
}
