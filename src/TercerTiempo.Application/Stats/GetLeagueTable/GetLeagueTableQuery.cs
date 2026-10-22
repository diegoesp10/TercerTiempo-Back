using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Stats.GetLeagueTable;

public sealed record GetLeagueTableQuery(Guid? CompetitionId) : IQuery<LeagueTableDto>;

internal sealed class GetLeagueTableHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    ICompetitionStore competitions,
    IStandingStore standings,
    ITeamStore teams,
    TimeProvider clock) : IQueryHandler<GetLeagueTableQuery, LeagueTableDto>
{
    public async Task<LeagueTableDto> Handle(GetLeagueTableQuery query, CancellationToken cancellationToken)
    {
        var (competition, season) = await ResolveAsync(query.CompetitionId, cancellationToken);
        if (season is null)
            return new LeagueTableDto(null, [], "Todavía no hay temporada en curso: la clasificación aparecerá cuando empiecen los partidos.");

        var table = await standings.GetBySeasonAsync(season.Id, cancellationToken);
        if (table.Count == 0)
            return new LeagueTableDto(SeasonDto.From(competition, season), [], "Todavía no hay partidos jugados esta temporada: la clasificación aparecerá con la primera jornada.");

        var teamMap = (await teams.GetByIdsAsync(table.Select(s => s.TeamId), cancellationToken)).ToDictionary(t => t.Id);

        var rows = table
            .OrderBy(s => s.Position)
            .Select(s => StandingDto.From(s, teamMap.GetValueOrDefault(s.TeamId)))
            .ToList();

        return new LeagueTableDto(SeasonDto.From(competition, season), rows, null);
    }

    private async Task<(Competition, Season?)> ResolveAsync(Guid? competitionId, CancellationToken cancellationToken)
    {
        if (competitionId is { } id)
        {
            var competition = await competitions.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, id);

            return (competition, competition.CurrentSeason);
        }

        var squad = currentUser.PlayerId is { } playerId
            ? await squads.GetByPlayerAsync(playerId, cancellationToken)
            : currentUser.CoachId is { } coachId
                ? (await squads.GetByCoachAsync(coachId, SquadAccess.Today(clock), cancellationToken)).FirstOrDefault()
                : null;

        if (squad is null)
            throw new ConflictException(ErrorCode.LeagueCompetitionRequired);

        return await Resolvers.SeasonOfSquadAsync(competitions, squad, cancellationToken);
    }
}
