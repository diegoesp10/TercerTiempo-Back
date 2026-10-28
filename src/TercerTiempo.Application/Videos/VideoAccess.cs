using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

internal static class VideoAccess
{
    public static VideoAccessLevel LevelFor(MatchVideo video, Squad? ownerSquad, ICurrentUser user, bool purchased, TimeProvider clock, bool clubOwner = false)
    {
        if (video.IsDeleted)
            return VideoAccessLevel.None;

        // El administrador los ve todos, sin comprarlos ni poder gestionarlos (canDelete va aparte).
        if (user.IsAdmin)
            return VideoAccessLevel.Admin;

        if (ownerSquad is not null && (clubOwner || SquadAccess.IsStaff(ownerSquad, user, clock) || SquadAccess.IsMember(ownerSquad, user)))
            return VideoAccessLevel.Owner;

        // Un vídeo público lo ve cualquiera que llegue al partido, sin cuenta ni pago.
        if (video.IsPublic || video.IsFree)
            return VideoAccessLevel.Free;

        if (!user.IsAuthenticated)
            return VideoAccessLevel.None;

        if (purchased)
            return VideoAccessLevel.Purchased;

        return user.CoachId is not null ? VideoAccessLevel.Locked : VideoAccessLevel.None;
    }

    public static async Task<bool> PurchasedAsync(MatchVideo video, Guid? coachId, IMatchVideoStore videos, IVideoPurchaseStore purchases, CancellationToken cancellationToken)
    {
        if (coachId is not { } id) return false;

        var siblings = (await videos.GetByMatchAsync(video.MatchId, cancellationToken))
            .Where(v => v.SquadId == video.SquadId)
            .Select(v => v.Id)
            .ToHashSet();
        siblings.Add(video.Id);

        return (await purchases.GetByCoachAsync(id, cancellationToken)).Any(p => siblings.Contains(p.VideoId));
    }

    public static bool CanWatch(VideoAccessLevel level) =>
        level is VideoAccessLevel.Owner or VideoAccessLevel.Purchased or VideoAccessLevel.Free or VideoAccessLevel.Admin;
}
