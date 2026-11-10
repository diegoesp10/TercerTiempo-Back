using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Matches.ListTeamMatches;
using TercerTiempo.Application.Teams;
using TercerTiempo.Application.Teams.GetTeam;
using TercerTiempo.Application.Teams.GetRosterPhoto;
using TercerTiempo.Application.Teams.GetTeamCrest;
using TercerTiempo.Application.Teams.ListTeams;

namespace TercerTiempo.Repository.Teams;

internal sealed class TeamsRepository(
    IQueryHandler<ListTeamsQuery, IReadOnlyList<TeamSummaryDto>> listTeams,
    IQueryHandler<GetTeamQuery, TeamDetailDto> getTeam,
    IQueryHandler<ListTeamMatchesQuery, IReadOnlyList<MatchDto>> listMatches,
    IQueryHandler<GetTeamCrestQuery, CrestFile?> getCrest,
    IQueryHandler<GetRosterPhotoQuery, CrestFile?> getRosterPhoto,
    IQueryHandler<Application.Players.Card.GetPlayerCardQuery, Application.Players.Card.PlayerCardDto> getPlayerCard) : ITeamsRepository
{
    public Task<IReadOnlyList<TeamSummaryDto>> ListAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        listTeams.Handle(new ListTeamsQuery(competitionId), cancellationToken);

    public Task<TeamDetailDto> GetAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        getTeam.Handle(new GetTeamQuery(teamId), cancellationToken);

    public Task<IReadOnlyList<MatchDto>> GetMatchesAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        listMatches.Handle(new ListTeamMatchesQuery(teamId), cancellationToken);

    public Task<CrestFile?> OpenCrestAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        getCrest.Handle(new GetTeamCrestQuery(teamId), cancellationToken);

    public Task<CrestFile?> OpenRosterPhotoAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default) =>
        getRosterPhoto.Handle(new GetRosterPhotoQuery(teamId, entryId), cancellationToken);

    public Task<Application.Players.Card.PlayerCardDto> GetPlayerCardAsync(Guid teamId, Guid entryId, CancellationToken cancellationToken = default) =>
        getPlayerCard.Handle(new Application.Players.Card.GetPlayerCardQuery(teamId, entryId), cancellationToken);
}
