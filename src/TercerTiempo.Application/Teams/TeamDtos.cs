using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Teams;

public sealed record TeamSummaryDto(
    Guid Id,
    string Name,
    string ShortName,
    string? CrestUrl,
    Guid CompetitionId,
    string CompetitionName,
    int? Position,
    int? Played,
    int? Points);

public sealed record TeamDetailDto(
    Guid Id,
    string Name,
    string ShortName,
    string? CrestUrl,
    SeasonDto Season,
    StandingDto? Standing,
    bool RosterPublished,
    IReadOnlyList<RosterEntryDto> Roster,
    IReadOnlyList<SquadMemberDto> RegisteredPlayers);

/// <summary>Jugador de la plantilla según el proveedor externo.</summary>
public sealed record RosterEntryDto(Guid Id, string FullName, Position? Position, int? ShirtNumber, bool Claimed);

/// <summary>Jugador registrado en la plataforma que pertenece a la plantilla.</summary>
public sealed record SquadMemberDto(Guid PlayerId, string DisplayName, Position Position, int? ShirtNumber, MembershipStatus Status);
