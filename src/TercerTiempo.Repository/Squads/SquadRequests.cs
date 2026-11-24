using System.ComponentModel;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Squads.Suggestions;
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
    [property: Description("Código del tipo (GET /api/squads/injury-types).")]
    string Type,
    [property: Description("Motivo o detalle, opcional.")]
    string? Reason,
    DateOnly StartedOn,
    [property: Description("Vuelta prevista; si falta se propone con la duración media del tipo.")]
    DateOnly? ExpectedReturnOn)
{
    internal RecordInjuryCommand ToRecord(Guid squadId) => new(squadId, SquadPlayerId ?? Guid.Empty, Type, Reason, StartedOn, ExpectedReturnOn);
    internal UpdateInjuryCommand ToUpdate(Guid squadId, Guid injuryId) => new(squadId, injuryId, Type, Reason, StartedOn, ExpectedReturnOn);
}

public sealed record ResolveSuggestionRequest(
    [property: Description("true aprueba y escribe la línea en el acta; false la rechaza.")]
    bool Approve,
    [property: Description("Nota para el jugador, opcional.")]
    string? Note)
{
    internal ResolveSuggestionCommand ToCommand(Guid squadId, Guid suggestionId) => new(squadId, suggestionId, Approve, Note);
}

public sealed record CloseInjuryRequest(
    [property: Description("Fecha del alta; por defecto hoy. No puede ser posterior a hoy ni anterior al parte.")]
    DateOnly? ReturnedOn);

public sealed record RelapseRequest(
    [property: Description("Fecha del parte de la recaída: no antes del alta de la lesión anterior ni después de hoy.")]
    DateOnly StartedOn,
    [property: Description("Fecha prevista de vuelta; si no se indica se propone con la duración media del tipo.")]
    DateOnly? ExpectedReturnOn,
    [property: Description("Motivo o detalle, opcional (hasta 300 caracteres).")]
    string? Reason)
{
    internal RecordRelapseCommand ToCommand(Guid squadId, Guid injuryId) => new(squadId, injuryId, StartedOn, ExpectedReturnOn, Reason);
}

public sealed record SquadVisibilityRequest(
    [property: Description("Private (solo cuerpo técnico y jugadores vinculados) o Public (cualquiera).")]
    SquadVisibility Visibility);

public sealed record SquadCallUpSizeRequest(
    [property: Description("A cuántos se convoca siempre (de 7 a 30); null: se pregunta en cada convocatoria.")]
    int? Size);

public sealed record MatchSheetRequest(IReadOnlyList<MatchSheetEntryInput> Entries)
{
    internal RecordMatchSheetCommand ToCommand(Guid squadId, Guid matchId) => new(squadId, matchId, Entries);
}
