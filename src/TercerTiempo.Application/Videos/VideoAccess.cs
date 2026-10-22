using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

internal static class VideoAccess
{
    public static VideoAccessLevel LevelFor(MatchVideo video, Squad? ownerSquad, ICurrentUser user, bool purchased, TimeProvider clock)
    {
        if (video.IsDeleted || !user.IsAuthenticated)
            return VideoAccessLevel.None;

        if (ownerSquad is not null && (SquadAccess.IsStaff(ownerSquad, user, clock) || SquadAccess.IsMember(ownerSquad, user)))
            return VideoAccessLevel.Owner;

        if (video.IsFree)
            return VideoAccessLevel.Free;

        if (purchased)
            return VideoAccessLevel.Purchased;

        return user.CoachId is not null ? VideoAccessLevel.Locked : VideoAccessLevel.None;
    }

    public static bool CanWatch(VideoAccessLevel level) =>
        level is VideoAccessLevel.Owner or VideoAccessLevel.Purchased or VideoAccessLevel.Free;
}
