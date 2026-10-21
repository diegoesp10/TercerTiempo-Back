using System.ComponentModel;
using TercerTiempo.Application.Squads.ManagePlayers;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Repository.Squads;

/// <summary>Cuerpo de las peticiones de gestión de plantilla (el id de la plantilla viaja en la ruta).</summary>
public sealed record SquadPlayerRequest(
    [property: Description("Nombre y apellidos del jugador.")]
    string FullName,
    [property: Description("Opcional: Goalkeeper, Defender, Midfielder o Forward.")]
    Position? Position,
    [property: Description("Opcional, 1-99.")]
    int? ShirtNumber)
{
    internal AddSquadPlayerCommand ToAdd(Guid squadId) => new(squadId, FullName, Position, ShirtNumber);
    internal UpdateSquadPlayerCommand ToUpdate(Guid squadId, Guid squadPlayerId) => new(squadId, squadPlayerId, FullName, Position, ShirtNumber);
}

public sealed record SquadVisibilityRequest(
    [property: Description("Private (solo cuerpo técnico y jugadores vinculados) o Public (cualquiera).")]
    SquadVisibility Visibility);

/// <summary>Acta completa de un partido: una línea por ficha (Played = false borra la línea si existía).</summary>
public sealed record MatchSheetRequest(IReadOnlyList<MatchSheetEntryInput> Entries)
{
    internal RecordMatchSheetCommand ToCommand(Guid squadId, Guid matchId) => new(squadId, matchId, Entries);
}
