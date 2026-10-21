using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

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
            ?? throw new ForbiddenException(ErrorCode.AuthPlayerAccountRequired);
    }

    public static async Task<Coach> CurrentCoachAsync(
        ICurrentUser currentUser, ICoachStore coaches, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return await coaches.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenException(ErrorCode.AuthCoachAccountRequired);
    }

    public static async Task<Club> CurrentClubAsync(
        ICurrentUser currentUser, IClubStore clubs, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return await clubs.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new ForbiddenException(ErrorCode.AuthClubAccountRequired);
    }

    /// <summary>Plantilla por id, comprobando que el usuario actual pertenece a su cuerpo técnico vigente.</summary>
    public static async Task<Squad> SquadForStaffAsync(
        ISquadStore squads, ICurrentUser currentUser, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);

        SquadAccess.EnsureStaff(squad, currentUser, clock);
        return squad;
    }

    /// <summary>Competición y temporada activa en la que participa un equipo.</summary>
    public static async Task<(Competition Competition, Season Season)> SeasonOfTeamAsync(
        ICompetitionStore competitions, Guid teamId, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByTeamAsync(teamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamActiveSeasonNotFound, teamId);

        var season = competition.CurrentSeason
            ?? throw new ConflictException(ErrorCode.TeamNoCurrentSeason);

        return (competition, season);
    }

    /// <summary>Competición y temporada de una plantilla.</summary>
    public static async Task<(Competition Competition, Season Season)> SeasonOfSquadAsync(
        ICompetitionStore competitions, Squad squad, CancellationToken cancellationToken) =>
        await SeasonByIdAsync(competitions, squad.SeasonId, cancellationToken);

    /// <summary>Competición y temporada a partir del id de la temporada.</summary>
    public static async Task<(Competition Competition, Season Season)> SeasonByIdAsync(
        ICompetitionStore competitions, Guid seasonId, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetBySeasonAsync(seasonId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, seasonId);

        var season = competition.Seasons.First(s => s.Id == seasonId);
        return (competition, season);
    }
}
