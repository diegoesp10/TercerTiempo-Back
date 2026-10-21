using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Matches;

/// <summary>Partidos del equipo de la plantilla con el número de líneas de acta registradas en cada uno.</summary>
public sealed record ListSquadMatchesQuery(Guid SquadId) : IQuery<IReadOnlyList<SquadMatchDto>>;

internal sealed class ListSquadMatchesHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IPerformanceStore performances,
    TimeProvider clock) : IQueryHandler<ListSquadMatchesQuery, IReadOnlyList<SquadMatchDto>>
{
    public async Task<IReadOnlyList<SquadMatchDto>> Handle(ListSquadMatchesQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException("Plantilla", query.SquadId);

        SquadAccess.EnsureCanView(squad, currentUser, clock);

        var list = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);

        var teamIds = list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        var recorded = (await performances.GetBySquadAsync(squad.Id, cancellationToken))
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.Count());

        return list
            .OrderBy(m => m.Matchday)
            .ThenBy(m => m.KickoffAt)
            .Select(m => new SquadMatchDto(MatchMapper.ToDto(m, teamMap), recorded.GetValueOrDefault(m.Id)))
            .ToList();
    }
}
