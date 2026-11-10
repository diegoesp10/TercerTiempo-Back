using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Competitions.Matchdays;

public sealed record GetCompetitionMatchdayQuery(Guid CompetitionId, int? Matchday) : IQuery<CompetitionMatchdayDto>;

public sealed record CompetitionMatchdayDto(
    Guid CompetitionId,
    string CompetitionName,
    [property: System.ComponentModel.Description("null si la competición aún no tiene temporada en curso.")]
    SeasonDto? Season,
    int? Matchday,
    int? CurrentMatchday,
    IReadOnlyList<MatchdaySummaryDto> Matchdays,
    IReadOnlyList<MatchDto> Matches,
    [property: System.ComponentModel.Description("Explicación cuando no hay partidos (temporada nueva o sin calendario).")]
    string? Notice);

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
    Seasons.ISeasonContext seasons,
    TimeProvider clock) : IQueryHandler<GetCompetitionMatchdayQuery, CompetitionMatchdayDto>
{
    public async Task<CompetitionMatchdayDto> Handle(GetCompetitionMatchdayQuery query, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(query.CompetitionId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.CompetitionNotFound, query.CompetitionId);

        // Temporada nueva o aún sin calendario: no es un error, solo no hay datos todavía.
        if (Common.Resolvers.SeasonFor(competition, await seasons.SelectedAsync(cancellationToken)) is not { } season)
            return new CompetitionMatchdayDto(competition.Id, competition.Name, null, null, null, [], [], "Todavía no hay temporada en curso: los partidos aparecerán cuando la federación publique el calendario.");

        var all = await matches.GetBySeasonAsync(season.Id, cancellationToken);
        if (all.Count == 0)
            return new CompetitionMatchdayDto(competition.Id, competition.Name, SeasonDto.From(competition, season), null, null, [], [], "Todavía no hay partidos de esta temporada: aparecerán cuando la federación publique el calendario.");

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
            selected.Select(m => MatchMapper.ToDto(m, teamMap)).ToList(),
            null);
    }
}
