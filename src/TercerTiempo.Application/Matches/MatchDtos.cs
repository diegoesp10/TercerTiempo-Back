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
    [property: System.ComponentModel.Description("Public = acepta solicitudes de acceso a cada partido; nada se ve sin un pase.")]
    SquadVisibility Visibility,
    bool Visible,
    [property: System.ComponentModel.Description("Quien mira es de dentro (club, cuerpo técnico, jugadores o administrador): puede abrir la plantilla y el comparador.")]
    bool Insider,
    IReadOnlyList<PerformanceDto> Entries,
    [property: System.ComponentModel.Description("Para los de fuera: lo que le abren sus pases y cómo pedir el acceso; null para los de dentro.")]
    MatchSheetAccessDto? Access = null);

public sealed record MatchSheetAccessDto(
    bool SheetPass,
    bool VideoPass,
    bool DownloadPass,
    [property: System.ComponentModel.Description("La plantilla es pública, tiene club con licencia y el partido ya se jugó: se puede solicitar.")]
    bool CanRequest,
    bool HasVideo,
    int SheetPriceCents,
    int VideoPriceCents,
    string Currency,
    [property: System.ComponentModel.Description("Estado de la última solicitud de quien mira para este partido.")]
    TercerTiempo.Domain.Passes.MatchPassRequestStatus? RequestStatus,
    [property: System.ComponentModel.Description("Pase aprobado para quien mira y todavía sin canjear.")]
    string? ReadyPassCode,
    int? ReadyPassPriceCents);

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
