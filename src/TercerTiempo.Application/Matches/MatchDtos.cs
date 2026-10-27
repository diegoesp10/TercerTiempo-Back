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

public sealed record MatchDetailDto(
    MatchDto Match,
    string? CompetitionName,
    string? SeasonLabel,
    [property: System.ComponentModel.Description("Actas internas de las plantillas de los dos equipos; las privadas llegan sin líneas (visible = false).")]
    IReadOnlyList<MatchSquadSheetDto> Sheets,
    [property: System.ComponentModel.Description("Equipo del partido que gestiona quien mira (su club o su cuerpo técnico vigente): ve el partido como el cuerpo técnico.")]
    Guid? ManagedTeamId = null,
    [property: System.ComponentModel.Description("Plantilla de ese equipo en la temporada del partido; vacía si el club todavía no la ha abierto (POST /api/clubs/me/teams/{teamId}/squad).")]
    Guid? ManagedSquadId = null);

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
