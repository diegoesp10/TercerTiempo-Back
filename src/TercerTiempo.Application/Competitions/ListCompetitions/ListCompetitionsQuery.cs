using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Seasons;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Application.Competitions.ListCompetitions;

public sealed record ListCompetitionsQuery : IQuery<IReadOnlyList<CompetitionDto>>;

internal sealed class ListCompetitionsHandler(
    ICompetitionStore competitions,
    ITerritoryStore territories,
    IStandingStore standings,
    ISeasonContext seasons,
    PublicDataCache cache) : IQueryHandler<ListCompetitionsQuery, IReadOnlyList<CompetitionDto>>
{
    public async Task<IReadOnlyList<CompetitionDto>> Handle(ListCompetitionsQuery query, CancellationToken cancellationToken)
    {
        var year = await seasons.SelectedAsync(cancellationToken);
        return await cache.GetAsync($"competitions:{year?.Id}", ct => LoadAsync(year, ct), cancellationToken);
    }

    private async Task<IReadOnlyList<CompetitionDto>> LoadAsync(SeasonYear? year, CancellationToken cancellationToken)
    {
        var all = await competitions.GetAllAsync(cancellationToken);
        var byId = (await territories.GetAllAsync(cancellationToken)).ToDictionary(t => t.Id);

        // Solo las ligas que se jugaron en la temporada elegida.
        var listed = all
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => (Competition: c, Season: Resolvers.SeasonFor(c, year)))
            .Where(x => year is null || x.Season is not null)
            .ToList();

        var teams = await standings.CountBySeasonsAsync(listed.Where(x => x.Season is not null).Select(x => x.Season!.Id), cancellationToken);
        return listed
            .Select(x => CompetitionDto.From(
                x.Competition, TerritoryDto.Of(x.Competition.TerritoryId, byId), x.Season, x.Season is null ? 0 : teams.GetValueOrDefault(x.Season.Id)))
            .ToList();
    }
}
