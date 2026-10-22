using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads;

public sealed record SquadSummaryDto(
    Guid Id,
    Guid TeamId,
    string TeamName,
    string TeamShortName,
    string? TeamCrestUrl,
    Guid CompetitionId,
    string CompetitionName,
    CompetitionLevel CompetitionLevel,
    Guid SeasonId,
    string SeasonLabel,
    SquadVisibility Visibility,
    int PlayersCount,
    int StaffCount,
    StaffRole? MyRole);

public sealed record SquadDetailDto(
    SquadSummaryDto Summary,
    string? InviteCode,
    bool CanManage,
    IReadOnlyList<SquadStaffDto> Staff,
    IReadOnlyList<SquadPlayerDto> Players);

public sealed record SquadStaffDto(Guid CoachId, string DisplayName, StaffRole Role, DateTimeOffset JoinedAt, DateOnly? ValidUntil);

public sealed record SquadPlayerDto(
    Guid Id,
    string FullName,
    Position? Position,
    int? ShirtNumber,
    bool FromRoster,
    bool Linked,
    string? LinkedPlayerName,
    SquadPlayerStatus Status,
    [property: System.ComponentModel.Description("Lesión abierta; solo la ven el cuerpo técnico y los jugadores vinculados.")]
    InjuryDto? Injury = null);

public sealed record SquadMatchDto(MatchDto Match, int EntriesRecorded);

public sealed record MatchSheetDto(
    SquadSummaryDto Squad,
    MatchDto Match,
    bool CanEdit,
    IReadOnlyList<MatchSheetEntryDto> Entries);

public sealed record MatchSheetEntryDto(SquadPlayerDto Player, PerformanceDto? Performance);

public sealed record SquadStatsDto(
    SquadSummaryDto Squad,
    int TeamMatchesPlayed,
    int MatchesRecorded,
    IReadOnlyList<SquadPlayerStatsDto> Players);

public sealed record SquadPlayerStatsDto(SquadPlayerDto Player, IndividualStatsDto Stats, TeamImpactDto Impact);

public sealed record SquadInviteDto(
    Guid SquadId,
    string TeamName,
    string CompetitionName,
    string SeasonLabel,
    IReadOnlyList<SquadPlayerDto> UnlinkedPlayers);
