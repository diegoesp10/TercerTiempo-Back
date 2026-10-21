using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Common;

/// <summary>
/// Política de acceso a los datos de una plantilla: su cuerpo técnico vigente (nombrado por el club y dentro del
/// periodo de validez) gestiona y ve todo; un jugador vinculado puede ver; el resto solo si la plantilla es pública.
/// </summary>
internal static class SquadAccess
{
    /// <summary>Fecha (UTC) con la que se evalúa la validez de los nombramientos.</summary>
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
            throw new ForbiddenException("Solo el cuerpo técnico vigente de la plantilla puede hacer esto.");
    }

    public static void EnsureCanView(Squad squad, ICurrentUser user, TimeProvider clock)
    {
        if (!CanView(squad, user, clock))
            throw new ForbiddenException("Los datos de esta plantilla son privados: solo su cuerpo técnico y sus jugadores pueden verlos.");
    }
}
