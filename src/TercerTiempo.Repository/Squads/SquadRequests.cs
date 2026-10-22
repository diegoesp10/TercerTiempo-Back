using System.ComponentModel;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Squads.ManagePlayers;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Repository.Squads;

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

public sealed record InjuryRequest(
    [property: Description("Jugador de la plantilla (solo al registrar; al editar se ignora).")]
    Guid? SquadPlayerId,
    InjuryType Type,
    [property: Description("Motivo o detalle, opcional.")]
    string? Reason,
    DateOnly StartedOn,
    [property: Description("Vuelta prevista; si falta se propone con la duración media del tipo.")]
    DateOnly? ExpectedReturnOn)
{
    internal RecordInjuryCommand ToRecord(Guid squadId) => new(squadId, SquadPlayerId ?? Guid.Empty, Type, Reason, StartedOn, ExpectedReturnOn);
    internal UpdateInjuryCommand ToUpdate(Guid squadId, Guid injuryId) => new(squadId, injuryId, Type, Reason, StartedOn, ExpectedReturnOn);
}

public sealed record CloseInjuryRequest(
    [property: Description("Fecha del alta; por defecto hoy.")]
    DateOnly? ReturnedOn);

public sealed record SquadVisibilityRequest(
    [property: Description("Private (solo cuerpo técnico y jugadores vinculados) o Public (cualquiera).")]
    SquadVisibility Visibility);

public sealed record MatchSheetRequest(IReadOnlyList<MatchSheetEntryInput> Entries)
{
    internal RecordMatchSheetCommand ToCommand(Guid squadId, Guid matchId) => new(squadId, matchId, Entries);
}
