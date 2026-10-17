using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Matches;

public sealed record TeamRefDto(Guid Id, string Name, string ShortName, string? CrestUrl);

public sealed record MatchDto(
    Guid Id,
    Guid SeasonId,
    int Matchday,
    DateTimeOffset KickoffAt,
    MatchStatus Status,
    TeamRefDto HomeTeam,
    TeamRefDto AwayTeam,
    int? HomeGoals,
    int? AwayGoals,
    string? Venue,
    Guid? MyPerformanceId);

public sealed record MatchDetailDto(
    MatchDto Match,
    PerformanceDto? MyPerformance,
    IReadOnlyList<PerformanceDto> Performances);

internal static class MatchMapper
{
    public static MatchDto ToDto(Match match, IReadOnlyDictionary<Guid, Team> teams, Guid? myPerformanceId) =>
        new(match.Id, match.SeasonId, match.Matchday, match.KickoffAt, match.Status,
            Ref(match.HomeTeamId, teams), Ref(match.AwayTeamId, teams),
            match.Score?.Home, match.Score?.Away, match.Venue, myPerformanceId);

    public static TeamRefDto Ref(Guid teamId, IReadOnlyDictionary<Guid, Team> teams) =>
        teams.TryGetValue(teamId, out var team)
            ? new TeamRefDto(team.Id, team.Name, team.ShortName, team.CrestUrl)
            : new TeamRefDto(teamId, "Equipo desconocido", "???", null);
}
