using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Squads;

internal static class SquadMapper
{
    public static SquadSummaryDto ToSummary(Squad squad, Team team, Competition competition, Season season, Guid? coachId, DateOnly today) =>
        new(squad.Id,
            team.Id, team.Name, team.ShortName, team.CrestLink(),
            competition.Id, competition.Name, competition.Level,
            season.Id, season.Label,
            squad.Visibility,
            squad.ActivePlayers.Count(),
            squad.StaffOn(today).Count(),
            coachId is { } c ? squad.StaffMemberOf(c, today)?.Role : null);

    public static async Task<SquadSummaryDto> ToSummaryAsync(
        Squad squad, Guid? coachId, TimeProvider clock, ITeamStore teams, ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.TeamNotFound, squad.TeamId);

        var (competition, season) = await Resolvers.SeasonOfSquadAsync(competitions, squad, cancellationToken);

        return ToSummary(squad, team, competition, season, coachId, SquadAccess.Today(clock));
    }

    public static SquadPlayerDto ToPlayerDto(SquadPlayer player, IReadOnlyDictionary<Guid, Player>? linkedPlayers, IReadOnlyDictionary<Guid, InjuryDto>? injuries = null) =>
        new(player.Id, player.FullName, player.Position, player.ShirtNumber,
            player.RosterEntryId is not null,
            player.IsLinked,
            player.PlayerId is { } id && linkedPlayers is not null && linkedPlayers.TryGetValue(id, out var linked) ? linked.DisplayName : null,
            player.Status,
            injuries?.GetValueOrDefault(player.Id));

    public static IEnumerable<SquadPlayer> Ordered(IEnumerable<SquadPlayer> players) =>
        players.OrderBy(p => p.ShirtNumber ?? int.MaxValue).ThenBy(p => p.FullName);

    public static async Task<SquadDetailDto> ToDetailAsync(
        Squad squad,
        ICurrentUser currentUser,
        TimeProvider clock,
        ICoachStore coaches,
        IPlayerStore players,
        ITeamStore teams,
        ICompetitionStore competitions,
        IInjuryStore injuries,
        CancellationToken cancellationToken)
    {
        var today = SquadAccess.Today(clock);
        var summary = await ToSummaryAsync(squad, currentUser.CoachId, clock, teams, competitions, cancellationToken);
        var canManage = SquadAccess.IsStaff(squad, currentUser, clock);

        var activeStaff = squad.StaffOn(today).ToList();
        var coachMap = (await coaches.GetByIdsAsync(activeStaff.Select(s => s.CoachId), cancellationToken))
            .ToDictionary(c => c.Id);

        var linkedIds = squad.Players.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);

        var staff = activeStaff
            .Select(s => new SquadStaffDto(s.CoachId, coachMap.GetValueOrDefault(s.CoachId)?.DisplayName ?? "Técnico", s.Role, s.JoinedAt, s.ValidUntil))
            .OrderBy(s => s.Role)
            .ThenBy(s => s.JoinedAt)
            .ToList();

        var activeInjuries = InjuryAccess.CanSee(squad, currentUser, clock)
            ? InjuryMapper.ActiveByPlayer(await injuries.GetBySquadAsync(squad.Id, cancellationToken), squad, today)
            : null;

        var squadPlayers = Ordered(squad.ActivePlayers)
            .Select(p => ToPlayerDto(p, linkedMap, activeInjuries))
            .ToList();

        return new SquadDetailDto(summary, canManage ? squad.InviteCode : null, canManage, staff, squadPlayers);
    }
}
