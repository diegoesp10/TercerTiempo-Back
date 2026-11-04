using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Passes;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Application.Videos;

// Un vídeo nunca es público: lo ven el club, el cuerpo técnico y los jugadores de la plantilla, el administrador y quien
// tenga un pase del partido que lo incluya.
internal static class VideoAccess
{
    public static VideoAccessLevel LevelFor(MatchVideo video, Squad? ownerSquad, ICurrentUser user, MatchInsight pass, TimeProvider clock, bool clubOwner = false)
    {
        if (video.IsDeleted)
            return VideoAccessLevel.None;

        if (user.IsAdmin)
            return VideoAccessLevel.Admin;

        if (ownerSquad is not null && (clubOwner || SquadAccess.IsStaff(ownerSquad, user, clock) || SquadAccess.IsMember(ownerSquad, user)))
            return VideoAccessLevel.Owner;

        if (pass.Video)
            return VideoAccessLevel.Pass;

        // De una plantilla pública se puede pedir el acceso; de una privada ni siquiera se enseña que haya vídeo.
        return ownerSquad?.Visibility == SquadVisibility.Public ? VideoAccessLevel.Locked : VideoAccessLevel.None;
    }

    // Descargar el archivo: quien lo gestiona (club y cuerpo técnico vigente) y quien tenga un pase con descarga.
    public static bool CanDownload(VideoAccessLevel level, bool manager, MatchInsight pass) =>
        (level == VideoAccessLevel.Owner && manager) || (level == VideoAccessLevel.Pass && pass.Download);

    public static bool CanWatch(VideoAccessLevel level) =>
        level is VideoAccessLevel.Owner or VideoAccessLevel.Pass or VideoAccessLevel.Admin;
}
