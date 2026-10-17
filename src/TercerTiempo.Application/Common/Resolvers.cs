using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Common;

/// <summary>Helpers compartidos por los handlers para resolver el jugador autenticado y la temporada activa.</summary>
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

    public static async Task<(Competition Competition, Season Season)> CurrentSeasonAsync(
        ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetCurrentAsync(cancellationToken)
            ?? throw new ConflictException("No hay ninguna temporada activa. Ejecuta primero la sincronización de datos.");

        var season = competition.CurrentSeason
            ?? throw new ConflictException("La competición no tiene una temporada marcada como actual.");

        return (competition, season);
    }
}
