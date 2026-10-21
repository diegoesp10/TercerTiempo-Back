using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

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
    TeamSquadDto? Squad,
    TeamProfileDto? Profile,
    string? OfficialUrl);

/// <summary>Ficha del club según la federación: sede, web, equipación y campo donde juega de local.</summary>
public sealed record TeamProfileDto(
    string? OfficialName,
    string? Locality,
    string? Province,
    string? Address,
    string? PostalCode,
    string? Website,
    string? Ground,
    string? GroundSurface,
    string? GroundAddress,
    string? GroundLocality,
    string? ShirtColor,
    string? ShortsColor,
    string? SocksColor)
{
    public static TeamProfileDto? From(TeamProfile? profile) => profile is null || profile.IsEmpty
        ? null
        : new TeamProfileDto(
            profile.OfficialName, profile.Locality, profile.Province, profile.Address, profile.PostalCode, profile.Website,
            profile.Ground, profile.GroundSurface, profile.GroundAddress, profile.GroundLocality,
            profile.ShirtColor, profile.ShortsColor, profile.SocksColor);
}

/// <summary>Jugador de la plantilla según el proveedor externo (o añadido/corregido por el club).</summary>
public sealed record RosterEntryDto(Guid Id, string FullName, Position? Position, int? ShirtNumber, bool ManagedByClub)
{
    public static RosterEntryDto From(RosterEntry entry) => new(entry.Id, entry.FullName, entry.Position, entry.ShirtNumber, entry.ManagedByClub);
}

/// <summary>Plantilla gestionada en la plataforma para este equipo y temporada, si existe.</summary>
public sealed record TeamSquadDto(Guid SquadId, SquadVisibility Visibility, int PlayersCount, int StaffCount, bool CanView, bool IsMine);
