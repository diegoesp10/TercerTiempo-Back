using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Seasons;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Application.Competitions.ListCompetitions;

public sealed record ListCompetitionsQuery : IQuery<IReadOnlyList<CompetitionDto>>;

internal sealed class ListCompetitionsHandler(
    ICompetitionStore competitions,
    IStandingStore standings,
    ISeasonContext seasons) : IQueryHandler<ListCompetitionsQuery, IReadOnlyList<CompetitionDto>>
{
    public async Task<IReadOnlyList<CompetitionDto>> Handle(ListCompetitionsQuery query, CancellationToken cancellationToken)
    {
        var all = await competitions.GetAllAsync(cancellationToken);
        var year = await seasons.SelectedAsync(cancellationToken);
        var result = new List<CompetitionDto>(all.Count);

        // Solo las ligas que se jugaron en la temporada elegida.
        foreach (var competition in all.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var season = Resolvers.SeasonFor(competition, year);
            if (year is not null && season is null) continue;

            var teamsCount = season is null ? 0 : (await standings.GetBySeasonAsync(season.Id, cancellationToken)).Count;
            result.Add(CompetitionDto.From(competition, season, teamsCount));
        }

        return result;
    }
}
