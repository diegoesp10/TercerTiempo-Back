using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

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
    TeamSquadDto? Squad);

/// <summary>Jugador de la plantilla según el proveedor externo.</summary>
public sealed record RosterEntryDto(Guid Id, string FullName, Position? Position, int? ShirtNumber);

/// <summary>Plantilla gestionada en la plataforma para este equipo y temporada, si existe.</summary>
public sealed record TeamSquadDto(Guid SquadId, SquadVisibility Visibility, int PlayersCount, int StaffCount, bool CanView, bool IsMine);
