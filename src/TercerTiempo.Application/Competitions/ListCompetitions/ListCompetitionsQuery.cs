using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Application.Competitions.ListCompetitions;

public sealed record ListCompetitionsQuery : IQuery<IReadOnlyList<CompetitionDto>>;

internal sealed class ListCompetitionsHandler(
    ICompetitionStore competitions,
    IStandingStore standings) : IQueryHandler<ListCompetitionsQuery, IReadOnlyList<CompetitionDto>>
{
    public async Task<IReadOnlyList<CompetitionDto>> Handle(ListCompetitionsQuery query, CancellationToken cancellationToken)
    {
        var all = await competitions.GetAllAsync(cancellationToken);
        var result = new List<CompetitionDto>(all.Count);

        foreach (var competition in all.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var teamsCount = competition.CurrentSeason is { } season
                ? (await standings.GetBySeasonAsync(season.Id, cancellationToken)).Count
                : 0;

            result.Add(CompetitionDto.From(competition, teamsCount));
        }

        return result;
    }
}
