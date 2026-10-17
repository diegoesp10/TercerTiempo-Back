using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Matches.GetMatch;

/// <summary>Detalle de un partido con las actas que el usuario puede ver (plantillas públicas o propias).</summary>
public sealed record GetMatchQuery(Guid MatchId) : IQuery<MatchDetailDto>;

internal sealed class GetMatchHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ITeamStore teams,
    ISquadStore squads,
    IPerformanceStore performances) : IQueryHandler<GetMatchQuery, MatchDetailDto>
{
    public async Task<MatchDetailDto> Handle(GetMatchQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException("Partido", query.MatchId);

        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken))
            .ToDictionary(t => t.Id);

        var matchSquads = await squads.GetByTeamIdsAsync([match.HomeTeamId, match.AwayTeamId], match.SeasonId, cancellationToken);
        var sheets = new List<MatchSquadSheetDto>();

        foreach (var squad in matchSquads.OrderBy(s => s.TeamId == match.HomeTeamId ? 0 : 1))
        {
            var visible = SquadAccess.CanView(squad, currentUser);
            var entries = new List<PerformanceDto>();

            if (visible)
            {
                var playerMap = squad.Players.ToDictionary(p => p.Id);
                entries = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
                    .Select(p => PerformanceMapper.ToDto(p, playerMap.GetValueOrDefault(p.SquadPlayerId)))
                    .OrderByDescending(p => p.Started)
                    .ThenByDescending(p => p.MinutesPlayed)
                    .ThenBy(p => p.ShirtNumber ?? int.MaxValue)
                    .ToList();
            }

            sheets.Add(new MatchSquadSheetDto(
                squad.Id, squad.TeamId, teamMap.GetValueOrDefault(squad.TeamId)?.Name ?? "Equipo",
                squad.Visibility, visible, entries));
        }

        return new MatchDetailDto(MatchMapper.ToDto(match, teamMap), sheets);
    }
}
