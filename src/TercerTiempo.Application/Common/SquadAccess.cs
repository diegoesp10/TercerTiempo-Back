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

    // El administrador ve cualquier plantilla (solo lectura: nunca es staff, miembro ni club de ninguna).
    public static bool CanView(Squad squad, ICurrentUser user, TimeProvider clock) =>
        user.IsAdmin || squad.CanBeViewedBy(user.CoachId, user.PlayerId, Today(clock));

    public static void EnsureCanView(Squad squad, ICurrentUser user, TimeProvider clock)
    {
        if (!CanView(squad, user, clock))
            throw new ForbiddenException(ErrorCode.SquadPrivate);
    }

    // El club que gestiona el equipo ve todo lo de su plantilla, como su cuerpo técnico.
    public static async Task<bool> IsClubOfAsync(Squad squad, ICurrentUser user, IClubStore clubs, CancellationToken cancellationToken) =>
        user.ClubId is { } clubId && (await clubs.GetByIdAsync(clubId, cancellationToken))?.Manages(squad.TeamId) == true;

    // El club es la entidad de la que depende el cuerpo técnico: gestiona la plantilla igual que un técnico vigente.
    public static async Task<bool> IsManagerAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        IsStaff(squad, user, clock) || await IsClubOfAsync(squad, user, clubs, cancellationToken);

    public static async Task<bool> CanViewAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        CanView(squad, user, clock) || await IsClubOfAsync(squad, user, clubs, cancellationToken);

    // Quien ve lo interno de la plantilla (lesiones, notas del técnico...). El administrador también, solo para leer.
    public static async Task<bool> IsInsiderAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken) =>
        user.IsAdmin || IsStaff(squad, user, clock) || IsMember(squad, user) || await IsClubOfAsync(squad, user, clubs, cancellationToken);

    public static async Task EnsureCanViewAsync(Squad squad, ICurrentUser user, TimeProvider clock, IClubStore clubs, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(squad, user, clock, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.SquadPrivate);
    }

    public static Task<SquadAccessRow?> AccessAsync(ISquadStore squads, ICurrentUser user, Guid squadId, TimeProvider clock, CancellationToken cancellationToken) =>
        squads.GetAccessAsync(squadId, user.CoachId, user.PlayerId, user.ClubId, Today(clock), cancellationToken);

    // Como IsInsiderAsync e IsManagerAsync, pero con la proyección y sin la plantilla entera: para las huellas.
    public static async Task<SquadAccessRow> InsiderAccessAsync(ISquadStore squads, ICurrentUser user, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(squads, user, squadId, clock, cancellationToken);
        if (!user.IsAdmin && !access.IsInsider) throw new ForbiddenException(ErrorCode.SquadPrivate);
        return access;
    }

    public static async Task<SquadAccessRow> ManagerAccessAsync(ISquadStore squads, ICurrentUser user, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(squads, user, squadId, clock, cancellationToken);
        if (!user.IsAdmin && !access.IsManager) throw new ForbiddenException(ErrorCode.SquadManagersOnly);
        return access;
    }

    private static async Task<SquadAccessRow> RequireAccessAsync(ISquadStore squads, ICurrentUser user, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated) throw new UnauthenticatedException();
        return await AccessAsync(squads, user, squadId, clock, cancellationToken) ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);
    }
}
