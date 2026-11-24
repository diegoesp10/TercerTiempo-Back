using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

public sealed class PlayerInjury : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    // Código del tipo en el catálogo; el tipo viene siempre con la lesión (nombre y duración media al día).
    public string Type { get; private set; } = null!;
    public InjuryTypeDefinition TypeDefinition { get; private set; } = null!;
    public string? Reason { get; private set; }
    // Fecha del parte: desde ese día cuentan los días de baja.
    public DateOnly StartedOn { get; private set; }
    public DateOnly? ExpectedReturnOn { get; private set; }
    public DateOnly? ReturnedOn { get; private set; }
    // La vuelta que se previó al dar el parte. La de ahora cambia con lo que diga el médico; esta se queda para comparar lo
    // previsto con lo que tardó de verdad.
    public DateOnly? InitialExpectedReturnOn { get; private set; }
    // Cuándo se registraron en la app el parte y el alta: no cambian al corregir las fechas.
    public DateTimeOffset ReportedAt { get; private set; }
    public DateTimeOffset? ReturnRecordedAt { get; private set; }
    // Recaída de una lesión ya superada: aquella conserva su alta y sus días.
    public Guid? RelapseOfInjuryId { get; private set; }
    // La de la temporada anterior que esta continúa: la misma lesión, que seguía abierta al cambiar de temporada. Conserva
    // su parte, así que los días de baja se siguen contando desde aquel día.
    public Guid? ContinuesInjuryId { get; private set; }
    // Cuándo pasó sin el alta a la temporada siguiente: desde entonces se lleva allí y aquí queda como historial.
    public DateTimeOffset? ContinuedAt { get; private set; }
    public Guid? RecordedByCoachId { get; private set; }
    public Guid? RecordedByClubId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Se junta con la otra ficha de la misma persona.
    public void MoveToSquadPlayer(Guid squadPlayerId) => SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId));

    public bool IsActive => ReturnedOn is null && ContinuedAt is null;

    public DateOnly? ContinuedOn => ContinuedAt is { } at ? SpainTime.Today(at) : null;

    public int? ExpectedDays => ExpectedReturnOn is { } expected ? expected.DayNumber - StartedOn.DayNumber : null;

    public int? InitialExpectedDays => InitialExpectedReturnOn is { } expected ? expected.DayNumber - StartedOn.DayNumber : null;

    private PlayerInjury() { }

    public static PlayerInjury Create(
        Squad squad, SquadPlayer player, SquadEditor editor, InjuryTypeDefinition type, string? reason, DateOnly startedOn, DateOnly? expectedReturnOn,
        DateTimeOffset now, PlayerInjury? relapseOf = null)
    {
        if (player.SquadId != squad.Id || !player.IsActive)
            throw new DomainException(ErrorCode.SquadPlayerNotMember);

        var injury = new PlayerInjury
        {
            Id = NewId(),
            SquadId = squad.Id,
            SquadPlayerId = player.Id,
            RecordedByCoachId = editor.CoachId,
            RecordedByClubId = editor.ClubId,
            ReportedAt = now,
        };

        if (relapseOf is not null)
        {
            if (relapseOf.SquadId != squad.Id || relapseOf.SquadPlayerId != player.Id)
                throw new DomainException(ErrorCode.InjuryNotFound, relapseOf.Id);
            relapseOf.EnsureNotContinued();
            if (relapseOf.ReturnedOn is not { } returned)
                throw new DomainException(ErrorCode.InjuryRelapseNeedsReturn);
            if (startedOn < returned)
                throw new DomainException(ErrorCode.InjuryRelapseBeforeReturn);

            // Es la misma lesión que vuelve: conserva su tipo aunque ya no se ofrezca para las nuevas.
            injury.Type = relapseOf.Type;
            injury.RelapseOfInjuryId = relapseOf.Id;
        }

        injury.Update(type, reason, startedOn, expectedReturnOn, now);
        return injury;
    }

    // Sigue abierta al empezar la temporada siguiente y la misma persona está en una plantilla del club de esa temporada:
    // pasa a ella con su parte, su tipo y su previsión (el contador sigue desde el parte), y esta queda como historial.
    public PlayerInjury ContinueIn(Squad squad, SquadPlayer player, DateTimeOffset now)
    {
        if (squad.Id == SquadId || player.SquadId != squad.Id || !player.IsActive)
            throw new DomainException(ErrorCode.SquadPlayerNotMember);

        var next = new PlayerInjury
        {
            Id = NewId(),
            SquadId = squad.Id,
            SquadPlayerId = player.Id,
            Type = Type,
            TypeDefinition = TypeDefinition,
            Reason = Reason,
            StartedOn = StartedOn,
            ExpectedReturnOn = ExpectedReturnOn,
            InitialExpectedReturnOn = InitialExpectedReturnOn,
            ReportedAt = ReportedAt,
            RecordedByCoachId = RecordedByCoachId,
            RecordedByClubId = RecordedByClubId,
            ContinuesInjuryId = Id,
            UpdatedAt = now,
        };
        PassOn(now);
        return next;
    }

    // Pasa sin el alta a la temporada siguiente. Sin continuación cuando allí ya llevan sus propias lesiones desde este
    // parte: no se duplica, pero tampoco se queda abierta para siempre en una temporada que ya acabó.
    public void PassOn(DateTimeOffset now)
    {
        if (!IsActive)
            throw new DomainException(ErrorCode.InjuryAlreadyClosed);

        ContinuedAt = now;
        UpdatedAt = now;
    }

    // La de la temporada anterior se ha borrado (se registró por error): esta sigue igual, ya sin enlace.
    public void ForgetContinues(DateTimeOffset now)
    {
        ContinuesInjuryId = null;
        UpdatedAt = now;
    }

    public void EnsureNotContinued()
    {
        if (ContinuedAt is not null)
            throw new DomainException(ErrorCode.InjuryContinued);
    }

    // Un tipo retirado del catálogo ya no se elige, pero la lesión que ya lo tenía se puede seguir corrigiendo con él.
    public void Update(InjuryTypeDefinition type, string? reason, DateOnly startedOn, DateOnly? expectedReturnOn, DateTimeOffset now)
    {
        EnsureNotContinued();
        if (!type.Active && type.Code != Type)
            throw new DomainException(ErrorCode.InjuryTypeRetired, type.Label);

        if (expectedReturnOn is { } expected && expected < startedOn)
            throw new DomainException(ErrorCode.InjuryDatesInvalid);

        if (ReturnedOn is { } returned && returned < startedOn)
            throw new DomainException(ErrorCode.InjuryReturnInvalid);

        Type = type.Code;
        TypeDefinition = type;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.NotEmpty(reason, "motivo", 300);
        StartedOn = startedOn;
        ExpectedReturnOn = expectedReturnOn;
        // Lo que se corrige el mismo día del parte es el propio parte; después, lo que cambia es la previsión.
        if (SpainTime.Today(ReportedAt) == SpainTime.Today(now))
            InitialExpectedReturnOn = expectedReturnOn;
        UpdatedAt = now;
    }

    public void Close(DateOnly returnedOn, DateTimeOffset now)
    {
        EnsureNotContinued();
        if (!IsActive)
            throw new DomainException(ErrorCode.InjuryAlreadyClosed);

        if (returnedOn < StartedOn)
            throw new DomainException(ErrorCode.InjuryReturnInvalid);

        ReturnedOn = returnedOn;
        ReturnRecordedAt = now;
        UpdatedAt = now;
    }

    // Deshacer un alta dada por error: la lesión vuelve a estar abierta, como si no se hubiera dado.
    public void Reopen(DateTimeOffset now)
    {
        EnsureNotContinued();
        ReturnedOn = null;
        ReturnRecordedAt = null;
        UpdatedAt = now;
    }

    // La lesión de la que era recaída se ha borrado (se registró por error): esta queda como una lesión sin más.
    public void ForgetRelapseOf(DateTimeOffset now)
    {
        RelapseOfInjuryId = null;
        UpdatedAt = now;
    }

    // De baja ese día: desde que empezó hasta la víspera del alta (sin alta, de ahí en adelante).
    public bool CoversDay(DateOnly day) => StartedOn <= day && (ReturnedOn is null || day < ReturnedOn);

    // Toca el periodo [from, to] (cualquiera de los dos abierto).
    public bool Overlaps(DateOnly? from, DateOnly? to) =>
        (to is null || StartedOn <= to) && (from is null || ReturnedOn is null || ReturnedOn > from);

    // La que pasó a la temporada siguiente cuenta aquí hasta ese día; el resto, en su continuación.
    public int DaysOut(DateOnly today)
    {
        var until = ReturnedOn ?? (ContinuedOn is { } passed && passed < today ? passed : today);
        return Math.Max(0, until.DayNumber - StartedOn.DayNumber);
    }
}

// Las lesiones de una plantilla para saber quién estaba de baja un día concreto: un lesionado no entrena ni va
// convocado. Se calcula al leer, así que corregir las fechas de una lesión corrige también lo pasado.
public sealed class InjuryCalendar
{
    private readonly ILookup<Guid, PlayerInjury> _byPlayer;

    public static readonly InjuryCalendar Empty = new([]);

    public InjuryCalendar(IEnumerable<PlayerInjury> injuries) =>
        _byPlayer = injuries.OrderByDescending(i => i.StartedOn).ToLookup(i => i.SquadPlayerId);

    public PlayerInjury? On(Guid squadPlayerId, DateOnly day) => _byPlayer[squadPlayerId].FirstOrDefault(i => i.CoversDay(day));

    public IReadOnlyList<PlayerInjury> Of(Guid squadPlayerId) => _byPlayer[squadPlayerId].ToList();
}
