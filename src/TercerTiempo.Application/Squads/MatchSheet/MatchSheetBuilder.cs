using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.MatchSheet;

internal static class MatchSheetBuilder
{
    public static async Task<MatchSheetDto> BuildAsync(
        Squad squad,
        Match match,
        ICurrentUser currentUser,
        TimeProvider clock,
        ITeamStore teams,
        ICompetitionStore competitions,
        IPlayerStore players,
        IPerformanceStore performances,
        ITeamMatchStatsStore teamStats,
        IClubStore clubs,
        CancellationToken cancellationToken)
    {
        var summary = await SquadMapper.ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);

        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);
        var matchDto = MatchMapper.ToDto(match, teamMap);

        var recorded = (await performances.GetBySquadAndMatchAsync(squad.Id, match.Id, cancellationToken))
            .ToDictionary(p => p.SquadPlayerId);

        var linkedIds = squad.Players.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);

        var entries = SquadMapper.Ordered(squad.Players.Where(p => p.IsActive || recorded.ContainsKey(p.Id)))
            .Select(p => new MatchSheetEntryDto(
                SquadMapper.ToPlayerDto(p, linkedMap),
                recorded.TryGetValue(p.Id, out var performance) ? PerformanceMapper.ToDto(performance, p) : null))
            .ToList();

        var canEdit = match.Status == MatchStatus.Played && await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken);
        var team = await teamStats.GetAsync(squad.Id, match.Id, cancellationToken);

        return new MatchSheetDto(summary, matchDto, canEdit, entries, team is null ? null : TeamStats.TeamMatchStatsMapper.ToDto(team));
    }
}
