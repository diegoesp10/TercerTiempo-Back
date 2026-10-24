using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.OfficialSheets;

public enum OfficialSheetAvailability
{
    NotPlayed,

    Pending,

    Available,

    Unavailable,

    // En cola para descargarse de la federación en segundo plano; el frontal vuelve a preguntar en unos segundos.
    Fetching,
}

public sealed record OfficialSheetDto(
    Guid MatchId,
    OfficialSheetAvailability Status,
    string? Provider,
    string? SheetCode,
    string? SourceUrl,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? FetchedAt,
    string? Referee,
    string? Message,
    OfficialTeamSheetDto? Home,
    OfficialTeamSheetDto? Away,
    IReadOnlyList<OfficialEventDto> Events);

public sealed record OfficialTeamSheetDto(
    MatchSide Side,
    string TeamName,
    Guid? TeamId,
    int? Goals,
    IReadOnlyList<OfficialLineDto> Starters,
    IReadOnlyList<OfficialLineDto> Bench);

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
            lines.Where(l => !l.Started).OrderBy(l => l.ShirtNumber ?? int.MaxValue).Select(ToDto).ToList());
    }

    public static OfficialSheetDto ToDto(OfficialMatchSheet sheet, Guid homeTeamId, Guid awayTeamId, OfficialSheetAvailability status, string? message) =>
        new(sheet.MatchId, status, sheet.Provider, sheet.ExternalId, sheet.SourceUrl, sheet.CheckedAt, sheet.FetchedAt, sheet.Referee, message,
            sheet.IsAvailable ? ToTeamDto(sheet, MatchSide.Home, homeTeamId) : null,
            sheet.IsAvailable ? ToTeamDto(sheet, MatchSide.Away, awayTeamId) : null,
            sheet.Events.OrderBy(e => e.Order)
                .Select(e => new OfficialEventDto(e.Side, e.Type, e.Minute, e.ExtraMinute, e.PlayerName, e.RelatedPlayerName))
                .ToList());

    public static OfficialSheetDto Empty(Guid matchId, OfficialSheetAvailability status, string? provider, string? message) =>
        new(matchId, status, provider, null, null, null, null, null, message, null, null, []);
}
