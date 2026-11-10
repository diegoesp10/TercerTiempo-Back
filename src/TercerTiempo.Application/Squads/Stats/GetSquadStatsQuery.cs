using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Squads.Stats;

public sealed record GetSquadStatsQuery(Guid SquadId) : IQuery<SquadStatsDto>;

internal sealed class GetSquadStatsHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    ICompetitionStore competitions,
    IPlayerStore players,
    IPerformanceStore performances,
    IClubStore clubs,
    TimeProvider clock) : IQueryHandler<GetSquadStatsQuery, SquadStatsDto>
{
    public async Task<SquadStatsDto> Handle(GetSquadStatsQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);

        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);
        var teamMatches = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
        var all = await performances.GetBySquadAsync(squad.Id, cancellationToken);
        var byPlayer = all.ToLookup(p => p.SquadPlayerId);

        var linkedIds = squad.Players.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);

        var rows = squad.Players
            .Where(p => p.IsActive || byPlayer.Contains(p.Id))
            .Select(p =>
            {
                var mine = byPlayer[p.Id].ToList();
                return new SquadPlayerStatsDto(
                    SquadMapper.ToPlayerDto(p, linkedMap),
                    PlayerStatsCalculator.Individual(mine),
                    PlayerStatsCalculator.TeamImpact(squad.TeamId, teamMatches, mine));
            })
            .OrderByDescending(r => r.Stats.Minutes)
            .ThenByDescending(r => r.Stats.GoalContributions)
            .ThenBy(r => r.Player.ShirtNumber ?? int.MaxValue)
            .ToList();

        return new SquadStatsDto(
            summary,
            teamMatches.Count(m => m.Status == MatchStatus.Played),
            all.Select(p => p.MatchId).Distinct().Count(),
            rows);
    }
}
