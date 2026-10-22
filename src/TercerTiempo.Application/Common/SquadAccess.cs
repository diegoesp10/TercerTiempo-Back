using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Common;

internal static class SquadAccess
{
    public static DateOnly Today(TimeProvider clock) => Squad.Today(clock.GetUtcNow());

    public static bool IsStaff(Squad squad, ICurrentUser user, TimeProvider clock) =>
        user.CoachId is { } coachId && squad.IsStaff(coachId, Today(clock));

    public static bool IsMember(Squad squad, ICurrentUser user) =>
        user.PlayerId is { } playerId && squad.IsMember(playerId);

    public static bool CanView(Squad squad, ICurrentUser user, TimeProvider clock) =>
        squad.CanBeViewedBy(user.CoachId, user.PlayerId, Today(clock));

    public static void EnsureStaff(Squad squad, ICurrentUser user, TimeProvider clock)
    {
        if (!user.IsAuthenticated)
            throw new UnauthenticatedException();

        if (!IsStaff(squad, user, clock))
            throw new ForbiddenException(ErrorCode.SquadStaffOnly);
    }

    public static void EnsureCanView(Squad squad, ICurrentUser user, TimeProvider clock)
    {
        if (!CanView(squad, user, clock))
            throw new ForbiddenException(ErrorCode.SquadPrivate);
    }
}
