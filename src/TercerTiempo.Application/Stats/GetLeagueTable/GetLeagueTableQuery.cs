using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Stats.GetLeagueTable;

/// <summary>Clasificación de una competición. Sin CompetitionId se usa la del equipo del jugador autenticado.</summary>
public sealed record GetLeagueTableQuery(Guid? CompetitionId) : IQuery<LeagueTableDto>;

internal sealed class GetLeagueTableHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ICompetitionStore competitions,
    IStandingStore standings,
    ITeamStore teams) : IQueryHandler<GetLeagueTableQuery, LeagueTableDto>
{
    public async Task<LeagueTableDto> Handle(GetLeagueTableQuery query, CancellationToken cancellationToken)
    {
        var (competition, season) = await ResolveAsync(query.CompetitionId, cancellationToken);

        var table = await standings.GetBySeasonAsync(season.Id, cancellationToken);
        var teamMap = (await teams.GetByIdsAsync(table.Select(s => s.TeamId), cancellationToken)).ToDictionary(t => t.Id);

        var rows = table
            .OrderBy(s => s.Position)
            .Select(s => StandingDto.From(s, teamMap.GetValueOrDefault(s.TeamId)))
            .ToList();

        return new LeagueTableDto(SeasonDto.From(competition, season), rows);
    }

    private async Task<(Competition, Season)> ResolveAsync(Guid? competitionId, CancellationToken cancellationToken)
    {
        if (competitionId is { } id)
        {
            var competition = await competitions.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException("Competición", id);

            var season = competition.CurrentSeason
                ?? throw new ConflictException("La competición no tiene una temporada en curso.");

            return (competition, season);
        }

        if (!currentUser.IsAuthenticated)
            throw new ConflictException("Indica la competición (competitionId) o inicia sesión para ver la de tu equipo.");

        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);

        var membership = player.CurrentMembership
            ?? throw new ConflictException("No perteneces a ninguna plantilla: indica la competición (competitionId).");

        return await Resolvers.SeasonOfMembershipAsync(competitions, membership, cancellationToken);
    }
}
