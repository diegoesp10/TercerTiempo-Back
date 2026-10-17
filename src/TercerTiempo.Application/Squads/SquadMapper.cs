using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads;

internal static class SquadMapper
{
    public static SquadSummaryDto ToSummary(Squad squad, Team team, Competition competition, Season season, Guid? coachId) =>
        new(squad.Id,
            team.Id, team.Name, team.ShortName, team.CrestUrl,
            competition.Id, competition.Name, competition.Level,
            season.Id, season.Label,
            squad.Visibility,
            squad.ActivePlayers.Count(),
            squad.ActiveStaff.Count(),
            coachId is { } c ? squad.ActiveStaff.FirstOrDefault(s => s.CoachId == c)?.Role : null);

    public static async Task<SquadSummaryDto> ToSummaryAsync(
        Squad squad, Guid? coachId, ITeamStore teams, ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var team = await teams.GetByIdAsync(squad.TeamId, cancellationToken)
            ?? throw new NotFoundException("Equipo", squad.TeamId);

        var (competition, season) = await Resolvers.SeasonOfSquadAsync(competitions, squad, cancellationToken);

        return ToSummary(squad, team, competition, season, coachId);
    }

    public static SquadPlayerDto ToPlayerDto(SquadPlayer player, IReadOnlyDictionary<Guid, Player>? linkedPlayers) =>
        new(player.Id, player.FullName, player.Position, player.ShirtNumber,
            player.RosterEntryId is not null,
            player.IsLinked,
            player.PlayerId is { } id && linkedPlayers is not null && linkedPlayers.TryGetValue(id, out var linked) ? linked.DisplayName : null,
            player.Status);

    public static IEnumerable<SquadPlayer> Ordered(IEnumerable<SquadPlayer> players) =>
        players.OrderBy(p => p.ShirtNumber ?? int.MaxValue).ThenBy(p => p.FullName);

    public static async Task<SquadDetailDto> ToDetailAsync(
        Squad squad,
        ICurrentUser currentUser,
        ICoachStore coaches,
        IPlayerStore players,
        ITeamStore teams,
        ICompetitionStore competitions,
        CancellationToken cancellationToken)
    {
        var summary = await ToSummaryAsync(squad, currentUser.CoachId, teams, competitions, cancellationToken);
        var canManage = SquadAccess.IsStaff(squad, currentUser);

        var coachMap = (await coaches.GetByIdsAsync(squad.ActiveStaff.Select(s => s.CoachId), cancellationToken))
            .ToDictionary(c => c.Id);

        var linkedIds = squad.Players.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var linkedMap = (await players.GetByIdsAsync(linkedIds, cancellationToken)).ToDictionary(p => p.Id);

        var staff = squad.ActiveStaff
            .Select(s => new SquadStaffDto(s.CoachId, coachMap.GetValueOrDefault(s.CoachId)?.DisplayName ?? "Técnico", s.Role, s.JoinedAt))
            .OrderBy(s => s.Role)
            .ThenBy(s => s.JoinedAt)
            .ToList();

        var squadPlayers = Ordered(squad.ActivePlayers)
            .Select(p => ToPlayerDto(p, linkedMap))
            .ToList();

        return new SquadDetailDto(summary, canManage ? squad.InviteCode : null, canManage, staff, squadPlayers);
    }
}
