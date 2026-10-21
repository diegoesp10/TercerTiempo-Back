using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Competitions.Matchdays;

/// <summary>Partidos de una jornada de la temporada en curso de una liga; sin número, la jornada en curso o la próxima.</summary>
public sealed record GetCompetitionMatchdayQuery(Guid CompetitionId, int? Matchday) : IQuery<CompetitionMatchdayDto>;

/// <param name="Matchday">Jornada devuelta (null si la temporada aún no tiene calendario).</param>
/// <param name="CurrentMatchday">Jornada en curso o la próxima por jugarse (null si no hay calendario).</param>
/// <param name="Matchdays">Todas las jornadas de la temporada con sus fechas, para navegar entre ellas.</param>
public sealed record CompetitionMatchdayDto(
    Guid CompetitionId,
    string CompetitionName,
    SeasonDto Season,
    int? Matchday,
    int? CurrentMatchday,
    IReadOnlyList<MatchdaySummaryDto> Matchdays,
    IReadOnlyList<MatchDto> Matches);

/// <param name="ReferenceAt">Fecha central de la jornada (mediana de sus partidos).</param>
public sealed record MatchdaySummaryDto(
    int Number,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset ReferenceAt,
    int Matches,
    int Played);

internal sealed class GetCompetitionMatchdayHandler(
    ICompetitionStore competitions,
    IMatchStore matches,
    ITeamStore teams,
    TimeProvider clock) : IQueryHandler<GetCompetitionMatchdayQuery, CompetitionMatchdayDto>
{
    public async Task<CompetitionMatchdayDto> Handle(GetCompetitionMatchdayQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, query.CompetitionId);

        var season = competition.CurrentSeason
            ?? throw new ConflictException(ErrorCode.CompetitionNoCurrentSeasonNamed, competition.Name);

        var all = await matches.GetBySeasonAsync(season.Id, cancellationToken);
        var summaries = MatchdayCalculator.Summaries(all);
        var current = MatchdayCalculator.Current(all, clock.GetUtcNow());

        var matchday = query.Matchday ?? current;
        if (query.Matchday is { } requested && summaries.All(s => s.Number != requested))
            throw new NotFoundException(ErrorCode.MatchdayNotFound, requested);

        var selected = all
            .Where(m => m.Matchday == matchday)
            .OrderBy(m => m.KickoffAt)
            .ThenBy(m => m.Id)
            .ToList();

        var teamIds = selected.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        return new CompetitionMatchdayDto(
            competition.Id,
            competition.Name,
            SeasonDto.From(competition, season),
            matchday,
            current,
            summaries,
            selected.Select(m => MatchMapper.ToDto(m, teamMap)).ToList());
    }
}
