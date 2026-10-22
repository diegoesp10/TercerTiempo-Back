using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Matches.GetMatch;

public sealed record GetMatchQuery(Guid MatchId) : IQuery<MatchDetailDto>;

internal sealed class GetMatchHandler(
    ICurrentUser currentUser,
    IMatchStore matches,
    ITeamStore teams,
    ISquadStore squads,
    IPerformanceStore performances,
    IClubStore clubs,
    ICompetitionStore competitions,
    ILicenseGate licenses,
    TimeProvider clock) : IQueryHandler<GetMatchQuery, MatchDetailDto>
{
    public async Task<MatchDetailDto> Handle(GetMatchQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);

        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken))
            .ToDictionary(t => t.Id);

        var matchSquads = await squads.GetByTeamIdsAsync([match.HomeTeamId, match.AwayTeamId], match.SeasonId, cancellationToken);
        var sheets = new List<MatchSquadSheetDto>();

        foreach (var squad in matchSquads.OrderBy(s => s.TeamId == match.HomeTeamId ? 0 : 1))
        {
            // Sin licencia (la del equipo, o la del espectador si no es de ese equipo) el acta interna no se muestra.
            var visible = await SquadAccess.CanViewAsync(squad, currentUser, clock, clubs, cancellationToken)
                && await licenses.CanAccessSquadAsync(squad, currentUser, cancellationToken);
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

        var competition = await competitions.GetBySeasonAsync(match.SeasonId, cancellationToken);
        var season = competition?.Seasons.FirstOrDefault(s => s.Id == match.SeasonId);
        return new MatchDetailDto(MatchMapper.ToDto(match, teamMap), competition?.Name, season?.Label, sheets);
    }
}
