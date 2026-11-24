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
    bool CanInvitePlayers,
    bool CanManage,
    IReadOnlyList<SquadStaffDto> Staff,
    [property: System.ComponentModel.Description("Los que están en la plantilla: solo con ellos se convoca, se entrena y se hacen actas.")]
    IReadOnlyList<SquadPlayerDto> Players,
    [property: System.ComponentModel.Description("Los que estuvieron y causaron baja (leftOn), los últimos primero: se consultan, pero ya no entran en nada nuevo.")]
    IReadOnlyList<SquadPlayerDto> LeftPlayers,
    [property: System.ComponentModel.Description("A cuántos se convoca siempre; null: se pregunta en cada convocatoria.")]
    int? CallUpSize = null);

public sealed record SquadStaffDto(Guid CoachId, string DisplayName, StaffRole Role, DateTimeOffset JoinedAt, DateOnly? ValidUntil, string? PhotoUrl = null);

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
    InjuryDto? Injury = null,
    string? InviteCode = null,
    [property: System.ComponentModel.Description("Foto de la ficha publicada por el club, si la ha subido.")]
    string? PhotoUrl = null,
    [property: System.ComponentModel.Description("Día en que causó baja (hora de España); null si sigue en la plantilla.")]
    DateOnly? LeftOn = null);

public sealed record SquadMatchDto(MatchDto Match, int EntriesRecorded);

public sealed record MatchSheetDto(
    SquadSummaryDto Squad,
    MatchDto Match,
    bool CanEdit,
    IReadOnlyList<MatchSheetEntryDto> Entries,
    [property: System.ComponentModel.Description("Datos del equipo en el partido (córners, posesión, tiros…); null si el cuerpo técnico no los ha apuntado.")]
    global::TercerTiempo.Application.Squads.TeamStats.TeamMatchStatsDto? TeamStats = null,
    [property: System.ComponentModel.Description("El partido tiene convocatoria con algún convocado (solo para el club y el cuerpo técnico): el acta se prepara con esos jugadores.")]
    bool HasCallUp = false);

public sealed record MatchSheetEntryDto(
    SquadPlayerDto Player,
    PerformanceDto? Performance,
    [property: System.ComponentModel.Description("Va en la convocatoria del partido (solo para el club y el cuerpo técnico).")]
    bool CalledUp = false,
    [property: System.ComponentModel.Description("Estaba sancionado para este partido: no puede salir como que jugó (la línea que ya tuviera se conserva).")]
    bool Suspended = false,
    global::TercerTiempo.Application.Squads.Suspensions.SuspensionBriefDto? Suspension = null);

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
    SquadPlayerDto Player,
    Guid TeamId,
    string? TeamCrestUrl);
