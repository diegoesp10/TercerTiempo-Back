using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Players.GetInvite;

public sealed record GetSquadInviteQuery(string InviteCode) : IQuery<SquadInviteDto>;

internal sealed class GetSquadInviteHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions) : IQueryHandler<GetSquadInviteQuery, SquadInviteDto>
{
    public async Task<SquadInviteDto> Handle(GetSquadInviteQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        if (await squads.GetByPlayerAsync(player.Id, cancellationToken) is not null)
            throw new ConflictException(ErrorCode.PlayerAlreadyInSquad);

        var squad = await squads.GetByInviteCodeAsync(InviteCodes.Normalize(query.InviteCode), cancellationToken)
            ?? throw new NotFoundException(ErrorCode.InviteCodeNotFound, query.InviteCode);
        var entry = squad.InvitedPlayer(query.InviteCode, player.DisplayName);

        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, squad.TeamId);

        var (competition, season) = await Resolvers.SeasonOfSquadAsync(competitions, squad, cancellationToken);

        return new SquadInviteDto(squad.Id, team.Name, competition.Name, season.Label, SquadMapper.ToPlayerDto(entry, null));
    }
}
