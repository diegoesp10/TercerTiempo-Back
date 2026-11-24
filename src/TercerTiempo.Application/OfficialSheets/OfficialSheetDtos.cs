using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads;
using System.ComponentModel;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;

namespace TercerTiempo.Application.OfficialSheets;

public enum OfficialSheetAvailability
{
    NotPlayed,

    Pending,

    Available,

    Unavailable,

    // En cola para descargarse de la federación en segundo plano; el frontal vuelve a preguntar en unos segundos.
    Fetching,

    // Alineaciones publicadas, pero el árbitro aún no ha cerrado el acta: sin goles, cambios, tarjetas ni minutos.
    Provisional,
}

public sealed record OfficialSheetDto(
    Guid MatchId,
    OfficialSheetAvailability Status,
    string? Provider,
    string? SheetCode,
    string? SourceUrl,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? FetchedAt,
    [property: Description("Quien pitó el partido (de las actas) o, si aún no se ha jugado, el designado por la federación; null si no se sabe ni se va a saber.")]
    SheetRefereesDto? Referees,
    string? Message,
    OfficialTeamSheetDto? Home,
    OfficialTeamSheetDto? Away,
    IReadOnlyList<OfficialEventDto> Events);

public sealed record SheetRefereesDto(
    [property: Description("true: lo que ha designado la federación para un partido por jugar (puede cambiar hasta el día del partido); false: quien lo pitó.")]
    bool Designated,
    [property: Description("Del árbitro principal al cuarto árbitro; vacío si la federación aún no ha designado a nadie.")]
    IReadOnlyList<SheetOfficialDto> Officials);

public sealed record SheetOfficialDto(
    OfficialRole Role,
    [property: Description("Nombre en el orden natural («Nombre Apellidos»).")]
    string Name,
    [property: Description("Su ficha de árbitro en la app; null si solo se conoce el nombre del acta.")]
    Guid? RefereeId);

public sealed record OfficialTeamSheetDto(
    MatchSide Side,
    string TeamName,
    Guid? TeamId,
    int? Goals,
    IReadOnlyList<OfficialLineDto> Starters,
    IReadOnlyList<OfficialLineDto> Bench,
    [property: System.ComponentModel.Description("Cuerpo técnico del equipo en el acta (entrenador primero); vacío si el acta no lo trae.")]
    IReadOnlyList<OfficialStaffDto>? Staff = null);

public sealed record OfficialStaffDto(
    OfficialStaffRole Role,
    [property: System.ComponentModel.Description("Función tal como la escribe la federación; si la misma persona figura en dos (delegado de campo y de equipo), las dos con «·».")]
    string RoleLabel,
    [property: System.ComponentModel.Description("Nombre como en el acta: «APELLIDOS, NOMBRE».")]
    string FullName,
    [property: System.ComponentModel.Description("Perfil del técnico en la app si el club lo ha nombrado en ese equipo y temporada con el mismo nombre; null si no.")]
    Guid? CoachId = null,
    [property: System.ComponentModel.Description("Amarillas que le enseñó el árbitro en el partido (0 a 2; van en la misma lista que las de los jugadores).")]
    int YellowCards = 0,
    [property: System.ComponentModel.Description("Expulsado: roja directa o doble amarilla.")]
    bool RedCard = false);

public sealed record OfficialLineDto(
    Guid Id,
    int? ShirtNumber,
    string FullName,
    bool Started,
    bool Played,
    int? MinuteIn,
    int? MinuteOut,
    int MinutesPlayed,
    int Goals,
    int PenaltyGoals,
    int OwnGoals,
    int YellowCards,
    bool RedCard);

public sealed record OfficialEventDto(
    MatchSide Side,
    OfficialEventType Type,
    int Minute,
    int? ExtraMinute,
    string PlayerName,
    string? RelatedPlayerName);

public sealed record SheetComparisonDto(
    SquadSummaryDto Squad,
    MatchDto Match,
    MatchSide Side,
    bool CanEdit,
    OfficialSheetDto Official,
    IReadOnlyList<SheetComparisonRowDto> Rows,
    IReadOnlyList<OfficialLineDto> UnmatchedOfficial,
    SheetComparisonSummaryDto Summary);

public sealed record SheetComparisonRowDto(
    SquadPlayerDto Player,
    PerformanceDto? Coach,
    OfficialLineDto? Official,
    IReadOnlyList<SheetDifferenceDto> Differences);

public sealed record SheetDifferenceDto(string Field, string? Coach, string? Official);

public sealed record SheetComparisonSummaryDto(
    int CoachLines,
    int OfficialLines,
    int Matched,
    int RowsWithDifferences,
    int Differences);

internal static class OfficialSheetMapper
{
    public const string ProvisionalMessage =
        "La federación ya ha publicado las alineaciones, pero el árbitro aún no ha cerrado el acta: faltan los goles, los cambios y las tarjetas. Se vuelve a consultar cada pocas horas.";

    public static OfficialLineDto ToDto(OfficialSheetLine line) =>
        new(line.Id, line.ShirtNumber, line.FullName, line.Started, line.Played, line.MinuteIn, line.MinuteOut,
            line.MinutesPlayed, line.Goals, line.PenaltyGoals, line.OwnGoals, line.YellowCards, line.RedCard);

    public static OfficialTeamSheetDto ToTeamDto(OfficialMatchSheet sheet, MatchSide side, Guid? teamId)
    {
        var lines = sheet.LinesOf(side).ToList();
        return new OfficialTeamSheetDto(
            side,
            side == MatchSide.Home ? sheet.HomeTeamName : sheet.AwayTeamName,
            teamId,
            side == MatchSide.Home ? sheet.HomeGoals : sheet.AwayGoals,
            lines.Where(l => l.Started).OrderBy(l => l.ShirtNumber ?? int.MaxValue).Select(ToDto).ToList(),
            lines.Where(l => !l.Started).OrderBy(l => l.ShirtNumber ?? int.MaxValue).Select(ToDto).ToList(),
            StaffOf(sheet, side));
    }

    // Una fila por persona (quien es delegado de campo y de equipo sale una vez con las dos funciones), del entrenador al
    // encargado de material.
    public static IReadOnlyList<OfficialStaffDto> StaffOf(OfficialMatchSheet sheet, MatchSide side) =>
        sheet.StaffOf(side)
            .GroupBy(s => s.NormalizedName)
            .Select(g => new OfficialStaffDto(
                g.Min(s => s.Role), string.Join(" · ", g.Select(s => s.RoleLabel).Distinct()), g.First().FullName,
                YellowCards: g.Max(s => s.YellowCards), RedCard: g.Any(s => s.RedCard)))
            .OrderBy(s => s.Role)
            .ToList();

    // Enlaza a su perfil a quien el club ha nombrado en el cuerpo técnico de ese equipo y temporada con el mismo nombre.
    public static OfficialSheetDto LinkStaff(OfficialSheetDto dto, IReadOnlyList<Domain.Clubs.CoachAppointment> appointments)
    {
        if (appointments.Count == 0) return dto;

        OfficialTeamSheetDto? Link(OfficialTeamSheetDto? team) => team?.Staff is not { Count: > 0 } staff ? team : team with
        {
            Staff = staff.Select(s => s with
            {
                CoachId = appointments
                    .Where(a => a.TeamId == team.TeamId && a.CoachId is not null && !a.IsRevoked)
                    .FirstOrDefault(a => Domain.Common.PersonName.SameAsSheet(s.FullName, a.NormalizedCoachName))?.CoachId,
            }).ToList(),
        };

        return dto with { Home = Link(dto.Home), Away = Link(dto.Away) };
    }

    public static OfficialSheetDto ToDto(OfficialMatchSheet sheet, Guid homeTeamId, Guid awayTeamId, OfficialSheetAvailability status, string? message) =>
        new(sheet.MatchId, status, sheet.Provider, sheet.ExternalId, sheet.SourceUrl, sheet.CheckedAt, sheet.FetchedAt, RefereeOf(sheet), message,
            Shows(sheet, status) ? ToTeamDto(sheet, MatchSide.Home, homeTeamId) : null,
            Shows(sheet, status) ? ToTeamDto(sheet, MatchSide.Away, awayTeamId) : null,
            sheet.Events.OrderBy(e => e.Order)
                .Select(e => new OfficialEventDto(e.Side, e.Type, e.Minute, e.ExtraMinute, e.PlayerName, e.RelatedPlayerName))
                .ToList());

    // El árbitro que trae el acta; quien la lee completa después con el equipo arbitral entero si ya se ha leído.
    private static SheetRefereesDto? RefereeOf(OfficialMatchSheet sheet) =>
        string.IsNullOrWhiteSpace(sheet.Referee)
            ? null
            : new SheetRefereesDto(false, [new SheetOfficialDto(OfficialRole.Referee, Referee.DisplayName(sheet.Referee), null)]);

    // Las alineaciones de un acta provisional solo se enseñan como tal, nunca dentro de otro estado.
    private static bool Shows(OfficialMatchSheet sheet, OfficialSheetAvailability status) =>
        sheet.IsAvailable || (sheet.IsProvisional && status == OfficialSheetAvailability.Provisional);

    public static OfficialSheetDto Empty(Guid matchId, OfficialSheetAvailability status, string? provider, string? message) =>
        new(matchId, status, provider, null, null, null, null, null, message, null, null, []);
}
