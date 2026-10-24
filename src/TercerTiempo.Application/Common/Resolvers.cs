using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Common;

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

    public static async Task<Squad> SquadForStaffAsync(
        ISquadStore squads, ICurrentUser currentUser, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);

        SquadAccess.EnsureStaff(squad, currentUser, clock);
        return squad;
    }

    // La liga y la temporada del equipo en la temporada de la app que se está viendo.
    public static async Task<(Competition Competition, Season Season)> SeasonOfTeamAsync(
        ICompetitionStore competitions, Seasons.ISeasonContext seasons, Guid teamId, CancellationToken cancellationToken)
    {
        var year = await seasons.SelectedAsync(cancellationToken);
        var competition = await competitions.GetByTeamAsync(teamId, year?.Id, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamActiveSeasonNotFound, teamId);

        var season = SeasonFor(competition, year)
            ?? throw new ConflictException(ErrorCode.TeamNoCurrentSeason);

        return (competition, season);
    }

    // Sin temporadas de la app todavía (base recién creada), la temporada en curso de cada liga.
    public static Season? SeasonFor(Competition competition, SeasonYear? year) =>
        year is null ? competition.CurrentSeason : competition.SeasonIn(year.Id);

    public static async Task<Squad> SquadForManagerAsync(
        ISquadStore squads, ICurrentUser currentUser, IClubStore clubs, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthenticatedException();
        var squad = await squads.GetByIdAsync(squadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, squadId);
        if (!SquadAccess.IsStaff(squad, currentUser, clock)
            && !await SquadAccess.IsClubOfAsync(squad, currentUser, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.SquadManagersOnly);
        return squad;
    }

    public static async Task<(Competition Competition, Season Season)> SeasonOfSquadAsync(
        ICompetitionStore competitions, Squad squad, CancellationToken cancellationToken) =>
        await SeasonByIdAsync(competitions, squad.SeasonId, cancellationToken);

    public static async Task<(Competition Competition, Season Season)> SeasonByIdAsync(
        ICompetitionStore competitions, Guid seasonId, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetBySeasonAsync(seasonId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, seasonId);

        var season = competition.Seasons.First(s => s.Id == seasonId);
        return (competition, season);
    }
}
