using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Players.Invites;

internal sealed record InviteMatch(Squad Squad, SquadPlayer Entry);

internal static class InviteLookup
{
    public static async Task<InviteMatch> FindAsync(string inviteCode, ISquadStore squads, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByInviteCodeAsync(InviteCodes.Normalize(inviteCode), cancellationToken)
            ?? throw new NotFoundException(ErrorCode.InviteCodeNotFound, inviteCode);
        return new InviteMatch(squad, squad.InvitedPlayer(inviteCode));
    }

    public static async Task<SquadInviteDto> DescribeAsync(
        InviteMatch match, ITeamStore teams, ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(match.Squad.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, match.Squad.TeamId);
        var (competition, season) = await Resolvers.SeasonOfSquadAsync(competitions, match.Squad, cancellationToken);
        return new SquadInviteDto(match.Squad.Id, team.Name, competition.Name, season.Label, SquadMapper.ToPlayerDto(match.Entry, null));
    }
}
