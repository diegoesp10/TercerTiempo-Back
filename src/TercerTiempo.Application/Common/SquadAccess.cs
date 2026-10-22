using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Clubs;
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

    // El club que gestiona el equipo ve todo lo de su plantilla, como su cuerpo técnico.
    public static async Task<bool> IsClubOfAsync(Squad squad, ICurrentUser user, IClubStore clubs, CancellationToken cancellationToken) =>
        user.ClubId is { } clubId && (await clubs.GetByIdAsync(clubId, cancellationToken))?.Manages(squad.TeamId) == true;

    public static async Task<bool> CanViewAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        CanView(squad, user, clock) || await IsClubOfAsync(squad, user, clubs, cancellationToken);

    public static async Task<bool> IsInsiderAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        IsStaff(squad, user, clock) || IsMember(squad, user) || await IsClubOfAsync(squad, user, clubs, cancellationToken);

    public static async Task EnsureCanViewAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(squad, user, clock, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.SquadPrivate);
    }
}
