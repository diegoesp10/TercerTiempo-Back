using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Common;

/// <summary>Helpers compartidos por los handlers para resolver la cuenta autenticada y las temporadas.</summary>
internal static class Resolvers
{
    public static async Task<Player> CurrentPlayerAsync(
        ICurrentUser currentUser, IPlayerStore players, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return await players.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenException("Esta acción requiere una cuenta de jugador.");
    }

    public static async Task<Coach> CurrentCoachAsync(
        ICurrentUser currentUser, ICoachStore coaches, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return await coaches.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenException("Esta acción requiere una cuenta del cuerpo técnico.");
    }

    /// <summary>Plantilla por id, comprobando que el usuario actual pertenece a su cuerpo técnico.</summary>
    public static async Task<Squad> SquadForStaffAsync(
        ISquadStore squads, ICurrentUser currentUser, Guid squadId, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException("Plantilla", squadId);

        SquadAccess.EnsureStaff(squad, currentUser);
        return squad;
    }

    /// <summary>Competición y temporada activa en la que participa un equipo.</summary>
    public static async Task<(Competition Competition, Season Season)> SeasonOfTeamAsync(
        ICompetitionStore competitions, Guid teamId, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByTeamAsync(teamId, cancellationToken)
            ?? throw new NotFoundException("Temporada activa del equipo", teamId);

        var season = competition.CurrentSeason
            ?? throw new ConflictException("La competición del equipo no tiene una temporada marcada como actual.");

        return (competition, season);
    }

    /// <summary>Competición y temporada de una plantilla.</summary>
    public static async Task<(Competition Competition, Season Season)> SeasonOfSquadAsync(
        ICompetitionStore competitions, Squad squad, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetBySeasonAsync(squad.SeasonId, cancellationToken)
            ?? throw new NotFoundException("Competición", squad.SeasonId);

        var season = competition.Seasons.First(s => s.Id == squad.SeasonId);
        return (competition, season);
    }
}
