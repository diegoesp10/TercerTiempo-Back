using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Common;

/// <summary>Helpers compartidos por los handlers para resolver el jugador autenticado y las temporadas.</summary>
internal static class Resolvers
{
    public static async Task<Player> CurrentPlayerAsync(
        ICurrentUser currentUser, IPlayerStore players, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return await players.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Jugador", userId);
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

    /// <summary>Competición y temporada a las que pertenece una pertenencia de plantilla.</summary>
    public static async Task<(Competition Competition, Season Season)> SeasonOfMembershipAsync(
        ICompetitionStore competitions, SquadMembership membership, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetBySeasonAsync(membership.SeasonId, cancellationToken)
            ?? throw new NotFoundException("Competición", membership.SeasonId);

        var season = competition.Seasons.First(s => s.Id == membership.SeasonId);
        return (competition, season);
    }
}
