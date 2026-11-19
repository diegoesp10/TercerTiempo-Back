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
    string? OfficialUrl,
    [property: System.ComponentModel.Description("Cuerpo técnico de la temporada según las actas oficiales (público); null si el equipo no tiene temporada.")]
    GetTeam.TeamStaffDto? Staff = null,
    [property: System.ComponentModel.Description("Página del equipo en la web que facilita calendario, resultados y plantilla (futbolasturiano.es); null si no viene de ella.")]
    string? SourceUrl = null);

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
    bool ManagedByClub,
    string? PhotoUrl,
    [property: System.ComponentModel.Description("Totales de la temporada según las actas oficiales; null si aún no aparece en ninguna.")]
    RosterEntryStatsDto? Stats,
    [property: System.ComponentModel.Description("Dorsales que ha llevado según las actas oficiales, del más usado al menos; vacío si aún no aparece en ninguna con dorsal. La ficha no tiene un dorsal fijo.")]
    IReadOnlyList<ShirtNumberUsageDto> ShirtNumbers)
{
    // Una fila por persona: si la web la publica en varias fichas, la principal con lo que las otras aporten.
    public static RosterEntryDto From(RosterPerson person, RosterEntryStatsDto? stats = null, IReadOnlyList<ShirtNumberUsageDto>? shirtNumbers = null) =>
        new(person.Main.Id, person.Main.FullName, person.Position, person.Entries.Any(e => e.ManagedByClub), person.WithPhoto?.PhotoLink(),
            stats, shirtNumbers ?? []);
}

public sealed record TeamSquadDto(Guid SquadId, SquadVisibility Visibility, int PlayersCount, int StaffCount, bool CanView, bool IsMine);
