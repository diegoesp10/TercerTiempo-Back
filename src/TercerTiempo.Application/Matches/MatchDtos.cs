using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

using TercerTiempo.Application.Teams;

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
    string? Venue);

public sealed record MatchDetailDto(MatchDto Match, IReadOnlyList<MatchSquadSheetDto> Sheets);

public sealed record MatchSquadSheetDto(
    Guid SquadId,
    Guid TeamId,
    string TeamName,
    SquadVisibility Visibility,
    bool Visible,
    IReadOnlyList<PerformanceDto> Entries);

internal static class MatchMapper
{
    public static MatchDto ToDto(Match match, IReadOnlyDictionary<Guid, Team> teams) =>
        new(match.Id, match.SeasonId, match.Matchday, match.KickoffAt, match.Status,
            Ref(match.HomeTeamId, teams), Ref(match.AwayTeamId, teams),
            match.Score?.Home, match.Score?.Away, match.Venue);

    public static TeamRefDto Ref(Guid teamId, IReadOnlyDictionary<Guid, Team> teams) =>
        teams.TryGetValue(teamId, out var team)
            ? new TeamRefDto(team.Id, team.Name, team.ShortName, team.CrestLink())
            : new TeamRefDto(teamId, "Equipo desconocido", "???", null);
}
