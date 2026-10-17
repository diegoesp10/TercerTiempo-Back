using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.ListTeams;

/// <summary>Equipos de la temporada activa ordenados por clasificación.</summary>
public sealed record ListTeamsQuery : IQuery<IReadOnlyList<TeamSummaryDto>>;

internal sealed class ListTeamsHandler(
    ICompetitionStore competitions,
    IStandingStore standings,
    ITeamStore teams) : IQueryHandler<ListTeamsQuery, IReadOnlyList<TeamSummaryDto>>
{
    public async Task<IReadOnlyList<TeamSummaryDto>> Handle(ListTeamsQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetCurrentAsync(cancellationToken);
        var season = competition?.CurrentSeason;

        var table = season is null
            ? []
            : await standings.GetBySeasonAsync(season.Id, cancellationToken);

        var byTeam = table.ToDictionary(s => s.TeamId);
        var all = await teams.GetAllAsync(cancellationToken);

        return all
            .Select(team =>
            {
                var standing = byTeam.GetValueOrDefault(team.Id);
                return new TeamSummaryDto(
                    team.Id, team.Name, team.ShortName, team.CrestUrl,
                    standing?.Position, standing?.Played, standing?.Points);
            })
            .OrderBy(t => t.Position ?? int.MaxValue)
            .ThenBy(t => t.Name)
            .ToList();
    }
}
