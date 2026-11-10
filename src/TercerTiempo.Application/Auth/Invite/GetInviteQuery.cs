using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Players.Invites;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Auth.Invite;

public sealed record GetInviteQuery(string InviteCode) : IQuery<SquadInviteDto>;

internal sealed class GetInviteHandler(ISquadStore squads, ITeamStore teams, ICompetitionStore competitions)
    : IQueryHandler<GetInviteQuery, SquadInviteDto>
{
    public async Task<SquadInviteDto> Handle(GetInviteQuery query, CancellationToken cancellationToken) =>
        await InviteLookup.DescribeAsync(await InviteLookup.FindAsync(query.InviteCode, squads, cancellationToken), teams, competitions, cancellationToken);
}
