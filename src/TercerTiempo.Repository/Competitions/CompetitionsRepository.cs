using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Competitions.ListCompetitions;

namespace TercerTiempo.Repository.Competitions;

internal sealed class CompetitionsRepository(
    IQueryHandler<ListCompetitionsQuery, IReadOnlyList<CompetitionDto>> listCompetitions) : ICompetitionsRepository
{
    public Task<IReadOnlyList<CompetitionDto>> ListAsync(CancellationToken cancellationToken = default) =>
        listCompetitions.Handle(new ListCompetitionsQuery(), cancellationToken);
}
