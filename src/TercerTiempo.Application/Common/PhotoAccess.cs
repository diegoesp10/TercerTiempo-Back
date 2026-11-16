using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Common;

// Las fotos son de jugadores, muchos de ellos menores: no se sirven a cualquiera que conozca la dirección. Las ven el
// club que gestiona el equipo, su cuerpo técnico y sus jugadores, y el administrador; nunca una visita sin sesión.
internal static class PhotoAccess
{
    public static async Task<bool> CanSeeTeamPhotosAsync(Guid teamId, Guid seasonId, ICurrentUser user, ISquadStore squads, IClubStore clubs, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (user.IsAdmin) return true;
        if (!user.IsAuthenticated) return false;
        if (user.ClubId is null && user.CoachId is null && user.PlayerId is null) return false;

        var squad = user.ClubId is null ? await squads.GetByTeamAndSeasonAsync(teamId, seasonId, cancellationToken) : null;
        return await CanSeeTeamPhotosAsync(teamId, squad, user, clubs, clock, cancellationToken);
    }

    // Con la plantilla de la temporada ya cargada (o null si el equipo no tiene).
    public static async Task<bool> CanSeeTeamPhotosAsync(Guid teamId, Squad? squad, ICurrentUser user, IClubStore clubs, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (user.IsAdmin) return true;
        if (!user.IsAuthenticated) return false;
        if (user.ClubId is { } clubId)
            return (await clubs.GetByIdAsync(clubId, cancellationToken))?.Manages(teamId) == true;
        return squad is not null && (SquadAccess.IsStaff(squad, user, clock) || SquadAccess.IsMember(squad, user));
    }

    // La foto que sube el propio jugador: además de su equipo, cualquier cuenta si él ha hecho público su perfil.
    public static async Task<bool> CanSeePlayerPhotoAsync(Player player, ICurrentUser user, ISquadStore squads, IClubStore clubs, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (user.IsAdmin || user.PlayerId == player.Id) return true;
        if (!user.IsAuthenticated) return false;
        if (player.PublicProfile) return true;

        var squad = await squads.GetByPlayerAsync(player.Id, cancellationToken);
        if (squad is null) return false;
        if (user.ClubId is { } clubId)
            return (await clubs.GetByIdAsync(clubId, cancellationToken))?.Manages(squad.TeamId) == true;
        return SquadAccess.IsStaff(squad, user, clock) || SquadAccess.IsMember(squad, user);
    }
}
