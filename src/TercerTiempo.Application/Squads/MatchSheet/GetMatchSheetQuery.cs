using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Squads.MatchSheet;

public sealed record GetMatchSheetQuery(Guid SquadId, Guid MatchId) : IQuery<MatchSheetDto>;

internal sealed class GetMatchSheetHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    ITeamMatchStatsStore teamStats,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetMatchSheetQuery, MatchSheetDto>
{
    public async Task<MatchSheetDto> Handle(GetMatchSheetQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);

        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        if (match.SeasonId != squad.SeasonId || !match.Involves(squad.TeamId))
            throw new ConflictException(ErrorCode.MatchNotOfSquad);

        return await MatchSheetBuilder.BuildAsync(squad, match, currentUser, clock, teams, competitions, players, performances, teamStats, clubs, cancellationToken);
    }
}
