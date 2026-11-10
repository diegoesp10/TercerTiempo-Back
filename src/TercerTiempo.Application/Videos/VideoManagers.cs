using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

internal static class VideoManagers
{
    public static async Task<(Guid? CoachId, Guid? ClubId, string Name)> CurrentAsync(
        ICurrentUser user, ICoachStore coaches, IClubStore clubs, CancellationToken ct)
    {
        if (user.ClubId != null)
        {
            var club = await Resolvers.CurrentClubAsync(user, clubs, ct);
            return (null, club.Id, club.Name);
        }
        var coach = await Resolvers.CurrentCoachAsync(user, coaches, ct);
        return (coach.Id, null, coach.DisplayName);
    }

    public static async Task<string> UploaderNameAsync(MatchVideo video, ICoachStore coaches, IClubStore clubs, CancellationToken ct) =>
        video.UploadedByClubId is { } clubId
            ? (await clubs.GetByIdAsync(clubId, ct))?.Name ?? "Club"
            : video.UploadedByCoachId is { } coachId
                ? (await coaches.GetByIdAsync(coachId, ct))?.DisplayName ?? "Cuerpo técnico"
                : "Equipo";
}
