using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.ListTeamMatches;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Teams.GetTeam;
using TercerTiempo.Application.Teams.ListTeams;

namespace TercerTiempo.Repository.Teams;

internal sealed class TeamsRepository(
    IQueryHandler<ListTeamsQuery, IReadOnlyList<TeamSummaryDto>> listTeams,
    IQueryHandler<GetTeamQuery, TeamDetailDto> getTeam,
    IQueryHandler<ListTeamMatchesQuery, IReadOnlyList<MatchDto>> listMatches) : ITeamsRepository
{
    public Task<IReadOnlyList<TeamSummaryDto>> ListAsync(CancellationToken cancellationToken = default) =>
        listTeams.Handle(new ListTeamsQuery(), cancellationToken);

    public Task<TeamDetailDto> GetAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        getTeam.Handle(new GetTeamQuery(teamId), cancellationToken);

    public Task<IReadOnlyList<MatchDto>> GetMatchesAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        listMatches.Handle(new ListTeamMatchesQuery(teamId), cancellationToken);
}
