using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Standings;
using TercerTiempo.Application.Teams.Roster;
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
    string? SocksColor,
    [property: System.ComponentModel.Description("Largo del terreno de juego en metros; lo apunta el club del equipo (null si no lo ha hecho).")]
    decimal? PitchLengthM = null,
    [property: System.ComponentModel.Description("Ancho del terreno de juego en metros.")]
    decimal? PitchWidthM = null)
{
    public static readonly TeamProfileDto Empty = new(null, null, null, null, null, null, null, null, null, null, null, null, null);

    public static TeamProfileDto? From(TeamProfile profile) => profile.IsEmpty
        ? null
        : new TeamProfileDto(
            profile.OfficialName, profile.Locality, profile.Province, profile.Address, profile.PostalCode, profile.Website,
            profile.Ground, profile.GroundSurface, profile.GroundAddress, profile.GroundLocality,
            profile.ShirtColor, profile.ShortsColor, profile.SocksColor, profile.PitchLengthM, profile.PitchWidthM);
}

public sealed record RosterEntryDto(
    Guid Id,
    string FullName,
    Position? Position,
    int? ShirtNumber,
    bool ManagedByClub,
    string? PhotoUrl,
    [property: System.ComponentModel.Description("Totales de la temporada según las actas oficiales; null si aún no aparece en ninguna.")]
    RosterEntryStatsDto? Stats)
{
    public static RosterEntryDto From(RosterEntry entry, RosterEntryStatsDto? stats = null) =>
        new(entry.Id, entry.FullName, entry.Position, entry.ShirtNumber, entry.ManagedByClub, entry.PhotoLink(), stats);
}

public sealed record TeamSquadDto(Guid SquadId, SquadVisibility Visibility, int PlayersCount, int StaffCount, bool CanView, bool IsMine);
