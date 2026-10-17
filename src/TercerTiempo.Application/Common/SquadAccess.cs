using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Common;

/// <summary>
/// Política de acceso a los datos de una plantilla: su cuerpo técnico gestiona y ve todo; un jugador vinculado
/// puede ver; el resto solo si la plantilla es pública.
/// </summary>
internal static class SquadAccess
{
    public static bool IsStaff(Squad squad, ICurrentUser user) =>
        user.CoachId is { } coachId && squad.IsStaff(coachId);

    public static bool IsMember(Squad squad, ICurrentUser user) =>
        user.PlayerId is { } playerId && squad.IsMember(playerId);

    public static bool CanView(Squad squad, ICurrentUser user) =>
        squad.CanBeViewedBy(user.CoachId, user.PlayerId);

    public static void EnsureStaff(Squad squad, ICurrentUser user)
    {
        if (!user.IsAuthenticated)
            throw new UnauthenticatedException();

        if (!IsStaff(squad, user))
            throw new ForbiddenException("Solo el cuerpo técnico de la plantilla puede hacer esto.");
    }

    public static void EnsureCanView(Squad squad, ICurrentUser user)
    {
        if (!CanView(squad, user))
            throw new ForbiddenException("Los datos de esta plantilla son privados: solo su cuerpo técnico y sus jugadores pueden verlos.");
    }
}
