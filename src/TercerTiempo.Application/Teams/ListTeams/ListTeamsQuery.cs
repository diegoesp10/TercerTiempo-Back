using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Teams.ListTeams;

/// <summary>Equipos de la temporada activa de una competición, ordenados por clasificación.</summary>
public sealed record ListTeamsQuery(Guid CompetitionId) : IQuery<IReadOnlyList<TeamSummaryDto>>;

internal sealed class ListTeamsHandler(
    ICompetitionStore competitions,
    IStandingStore standings,
    ITeamStore teams) : IQueryHandler<ListTeamsQuery, IReadOnlyList<TeamSummaryDto>>
{
    public async Task<IReadOnlyList<TeamSummaryDto>> Handle(ListTeamsQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, query.CompetitionId);

        if (competition.CurrentSeason is not { } season)
            return [];

        var table = (await standings.GetBySeasonAsync(season.Id, cancellationToken)).ToDictionary(s => s.TeamId);
        var seasonTeams = await teams.GetBySeasonAsync(season.Id, cancellationToken);

        return seasonTeams
            .Select(team =>
            {
                var standing = table.GetValueOrDefault(team.Id);
                return new TeamSummaryDto(
                    team.Id, team.Name, team.ShortName, team.CrestLink(),
                    competition.Id, competition.Name,
                    standing?.Position, standing?.Played, standing?.Points);
            })
            .OrderBy(t => t.Position ?? int.MaxValue)
            .ThenBy(t => t.Name)
            .ToList();
    }
}
