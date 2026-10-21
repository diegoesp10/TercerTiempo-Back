using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads;

/// <summary>Plantilla gestionada por un cuerpo técnico, con su equipo, competición y temporada.</summary>
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

/// <summary>Detalle de la plantilla. InviteCode solo se devuelve al cuerpo técnico.</summary>
public sealed record SquadDetailDto(
    SquadSummaryDto Summary,
    string? InviteCode,
    bool CanManage,
    IReadOnlyList<SquadStaffDto> Staff,
    IReadOnlyList<SquadPlayerDto> Players);

/// <summary>Miembro vigente del cuerpo técnico; ValidUntil es el último día de su nombramiento.</summary>
public sealed record SquadStaffDto(Guid CoachId, string DisplayName, StaffRole Role, DateTimeOffset JoinedAt, DateOnly? ValidUntil);

public sealed record SquadPlayerDto(
    Guid Id,
    string FullName,
    Position? Position,
    int? ShirtNumber,
    bool FromRoster,
    bool Linked,
    string? LinkedPlayerName,
    SquadPlayerStatus Status);

/// <summary>Partido del equipo de la plantilla con cuántas líneas de acta tiene registradas.</summary>
public sealed record SquadMatchDto(MatchDto Match, int EntriesRecorded);

/// <summary>Acta de un partido: una línea por ficha de la plantilla (sin rendimiento = no jugó o no está rellenado).</summary>
public sealed record MatchSheetDto(
    SquadSummaryDto Squad,
    MatchDto Match,
    bool CanEdit,
    IReadOnlyList<MatchSheetEntryDto> Entries);

public sealed record MatchSheetEntryDto(SquadPlayerDto Player, PerformanceDto? Performance);

/// <summary>Estadísticas de temporada de toda la plantilla, jugador a jugador.</summary>
public sealed record SquadStatsDto(
    SquadSummaryDto Squad,
    int TeamMatchesPlayed,
    int MatchesRecorded,
    IReadOnlyList<SquadPlayerStatsDto> Players);

public sealed record SquadPlayerStatsDto(SquadPlayerDto Player, IndividualStatsDto Stats, TeamImpactDto Impact);

/// <summary>Lo que ve un jugador al introducir un código de invitación: la plantilla y las fichas aún sin vincular.</summary>
public sealed record SquadInviteDto(
    Guid SquadId,
    string TeamName,
    string CompetitionName,
    string SeasonLabel,
    IReadOnlyList<SquadPlayerDto> UnlinkedPlayers);
