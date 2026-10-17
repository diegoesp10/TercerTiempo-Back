using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Stats.GetLeagueTable;

public sealed record GetLeagueTableQuery : IQuery<LeagueTableDto>;

internal sealed class GetLeagueTableHandler(
    ICompetitionStore competitions,
    IStandingStore standings,
    ITeamStore teams) : IQueryHandler<GetLeagueTableQuery, LeagueTableDto>
{
    public async Task<LeagueTableDto> Handle(GetLeagueTableQuery query, CancellationToken cancellationToken)
    {
        var (competition, season) = await Resolvers.CurrentSeasonAsync(competitions, cancellationToken);

        var table = await standings.GetBySeasonAsync(season.Id, cancellationToken);
        var teamMap = (await teams.GetByIdsAsync(table.Select(s => s.TeamId), cancellationToken)).ToDictionary(t => t.Id);

        var rows = table
            .OrderBy(s => s.Position)
            .Select(s => StandingDto.From(s, teamMap.GetValueOrDefault(s.TeamId)))
            .ToList();

        return new LeagueTableDto(SeasonDto.From(competition, season), rows);
    }
}
