using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Performances;

public static class PerformancePolicy
{
    public static void EnsureCanRecord(Squad squad, Guid coachId, Match match, DateOnly today)
    {
        if (!squad.IsStaff(coachId, today))
            throw new DomainException(ErrorCode.SheetStaffOnly);

        if (match.SeasonId != squad.SeasonId)
            throw new DomainException(ErrorCode.SheetMatchOtherSeason);

        if (!match.Involves(squad.TeamId))
            throw new DomainException(ErrorCode.SheetTeamNotInvolved);

        if (match.Status != MatchStatus.Played)
            throw new DomainException(ErrorCode.SheetMatchNotPlayed);
    }
}
