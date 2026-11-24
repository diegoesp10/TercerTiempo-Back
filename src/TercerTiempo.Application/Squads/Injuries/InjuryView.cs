using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

// Lo justo de una lesión para explicar por qué un jugador no entrena o no va convocado.
public sealed record InjuryBriefDto(
    Guid Id,
    [property: Description("Código del tipo en el catálogo de lesiones.")]
    string Type,
    string TypeLabel,
    string? Reason,
    [property: Description("Fecha del parte: desde ese día cuentan los días de baja.")]
    DateOnly StartedOn,
    [property: Description("Vuelta prevista; null si no se sabe.")]
    DateOnly? ExpectedReturnOn,
    [property: Description("Día del alta (ya disponible); null si sigue lesionado.")]
    DateOnly? ReturnedOn,
    [property: Description("Días de baja contados desde el parte: hasta hoy si sigue lesionado, hasta el alta si ya la tiene.")]
    int DaysOut = 0);

// Quién estaba lesionado cada día y quién puede ver el detalle: el club, el cuerpo técnico y el administrador ven el de
// todos; un jugador, solo el suyo. Los compañeros solo ven que no está disponible.
internal sealed class InjuryView(InjuryCalendar calendar, Func<Guid, bool> details, DateOnly today)
{
    public static readonly InjuryView None = new(InjuryCalendar.Empty, _ => false, DateOnly.MinValue);

    public InjuryCalendar Calendar => calendar;

    public PlayerInjury? On(Guid squadPlayerId, DateOnly day) => calendar.On(squadPlayerId, day);

    public bool CanSeeDetails(Guid squadPlayerId) => details(squadPlayerId);

    public InjuryBriefDto? BriefFor(Guid squadPlayerId, PlayerInjury? injury) =>
        injury is not null && details(squadPlayerId) ? Brief(injury, today) : null;

    public static InjuryBriefDto Brief(PlayerInjury injury, DateOnly today) =>
        new(injury.Id, injury.Type, injury.TypeDefinition.Label, injury.Reason, injury.StartedOn, injury.ExpectedReturnOn, injury.ReturnedOn,
            injury.DaysOut(today));

    public static async Task<InjuryView> LoadAsync(
        Squad squad, ICurrentUser currentUser, TimeProvider clock, IClubStore clubs, IInjuryStore injuries, CancellationToken cancellationToken)
    {
        var list = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
        if (list.Count == 0) return None;
        var visible = await InjuryAccess.VisibleSquadPlayersAsync(squad, currentUser, clock, clubs, cancellationToken);
        return new InjuryView(new InjuryCalendar(list), visible, SquadAccess.Today(clock));
    }
}
