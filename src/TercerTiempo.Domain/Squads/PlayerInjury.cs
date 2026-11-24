using System.Globalization;
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
    // Dónde fue en el cuerpo (lo que pregunta su tipo: el músculo, el hueso, la rodilla...) y el lado, si tiene dos. Las
    // lesiones de antes de que se preguntara no lo tienen.
    public string? SiteCode { get; private set; }
    public BodySite? Site { get; private set; }
    public BodySide? Side { get; private set; }
    // El grado (en los tipos que lo tienen) y cómo se produjo, si se sabe.
    public InjuryGrade? Grade { get; private set; }
    public InjuryMechanism? Mechanism { get; private set; }
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
    // Dónde se lesionó: el partido o el entrenamiento (uno u otro; ninguno si fue en otro sitio o no se sabe), con el día y
    // el nombre que tenían al registrarlo.
    public Guid? MatchId { get; private set; }
    public Guid? TrainingSessionId { get; private set; }
    public DateOnly? OccasionDay { get; private set; }
    public string? OccasionLabel { get; private set; }
    public Guid? RecordedByCoachId { get; private set; }
    public Guid? RecordedByClubId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Se junta con la otra ficha de la misma persona.
    public void MoveToSquadPlayer(Guid squadPlayerId) => SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId));

    public bool IsActive => ReturnedOn is null && ContinuedAt is null;

    public DateOnly? ContinuedOn => ContinuedAt is { } at ? SpainTime.Today(at) : null;

    public int? ExpectedDays => ExpectedReturnOn is { } expected ? expected.DayNumber - StartedOn.DayNumber : null;

    public int? InitialExpectedDays => InitialExpectedReturnOn is { } expected ? expected.DayNumber - StartedOn.DayNumber : null;

    public Guid? OccasionId => MatchId ?? TrainingSessionId;

    public InjuryDetail Detail => new(Site, Side, Grade, Mechanism);

    // «Bíceps femoral izquierdo», «Menisco interno, rodilla derecha»; null si no se sabe dónde fue.
    public string? Location => InjuryLocation.Describe(TypeDefinition, Site, Side);

    public InjuryOccasion? Occasion => OccasionDay is not { } day ? null
        : MatchId is { } match ? new InjuryOccasion(InjuryOccasionKind.Match, match, day, OccasionLabel ?? string.Empty)
        : TrainingSessionId is { } session ? new InjuryOccasion(InjuryOccasionKind.Training, session, day, OccasionLabel ?? string.Empty)
        : null;

    private PlayerInjury() { }

    public static PlayerInjury Create(
        Squad squad, SquadPlayer player, SquadEditor editor, InjuryTypeDefinition type, string? reason, DateOnly startedOn, DateOnly? expectedReturnOn,
        DateTimeOffset now, PlayerInjury? relapseOf = null, InjuryOccasion? occasion = null, InjuryDetail? detail = null)
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

            // Es la misma lesión que vuelve: conserva su tipo y su sitio aunque ya no se ofrezcan para las nuevas.
            injury.Type = relapseOf.Type;
            injury.SiteCode = relapseOf.SiteCode;
            injury.RelapseOfInjuryId = relapseOf.Id;
        }

        injury.Update(type, reason, startedOn, expectedReturnOn, now, occasion, detail ?? relapseOf?.Detail ?? InjuryDetail.None);
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
            SiteCode = SiteCode,
            Site = Site,
            Side = Side,
            Grade = Grade,
            Mechanism = Mechanism,
            Reason = Reason,
            StartedOn = StartedOn,
            ExpectedReturnOn = ExpectedReturnOn,
            InitialExpectedReturnOn = InitialExpectedReturnOn,
            ReportedAt = ReportedAt,
            MatchId = MatchId,
            TrainingSessionId = TrainingSessionId,
            OccasionDay = OccasionDay,
            OccasionLabel = OccasionLabel,
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

    // Un tipo retirado del catálogo ya no se elige, pero la lesión que ya lo tenía se puede seguir corrigiendo con él (lo
    // mismo su sitio). Sin detalle, se queda el que tiene.
    public void Update(
        InjuryTypeDefinition type, string? reason, DateOnly startedOn, DateOnly? expectedReturnOn, DateTimeOffset now, InjuryOccasion? occasion = null,
        InjuryDetail? detail = null)
    {
        EnsureNotContinued();
        if (!type.Active && type.Code != Type)
            throw new DomainException(ErrorCode.InjuryTypeRetired, type.Label);
        var resolved = ResolveDetail(type, detail ?? Detail);

        if (expectedReturnOn is { } expected && expected < startedOn)
            throw new DomainException(ErrorCode.InjuryDatesInvalid);

        if (ReturnedOn is { } returned && returned < startedOn)
            throw new DomainException(ErrorCode.InjuryReturnInvalid);

        if (occasion is not null && (occasion.Id == Guid.Empty || !Enum.IsDefined(occasion.Kind)))
            throw new DomainException(ErrorCode.InjuryOccasionInvalid);

        // El parte es de ese día o de después: antes de lesionarse no estaba de baja.
        if (occasion is not null && startedOn < occasion.Day)
            throw new DomainException(ErrorCode.InjuryBeforeOccasion, occasion.Day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

        Type = type.Code;
        TypeDefinition = type;
        SiteCode = resolved.Site?.Code;
        Site = resolved.Site;
        Side = resolved.Side;
        Grade = resolved.Grade;
        Mechanism = resolved.Mechanism;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : Guard.NotEmpty(reason, "motivo", 300);
        StartedOn = startedOn;
        ExpectedReturnOn = expectedReturnOn;
        SetOccasion(occasion);
        // Lo que se corrige el mismo día del parte es el propio parte; después, lo que cambia es la previsión.
        if (SpainTime.Today(ReportedAt) == SpainTime.Today(now))
            InitialExpectedReturnOn = expectedReturnOn;
        UpdatedAt = now;
    }

    // Lo que pregunta el tipo, y nada más: el sitio (si el tipo tiene uno fijo y no se precisa, ese), el lado si el sitio
    // tiene dos, el grado si el tipo lo lleva y cómo se produjo. El sitio que ya tenía con el mismo tipo vale aunque
    // después se haya retirado o sacado de la lista del tipo.
    private InjuryDetail ResolveDetail(InjuryTypeDefinition type, InjuryDetail detail)
    {
        if ((detail.Side is { } side && !Enum.IsDefined(side))
            || (detail.Grade is { } grade && !Enum.IsDefined(grade))
            || (detail.Mechanism is { } mechanism && !Enum.IsDefined(mechanism)))
            throw new DomainException(ErrorCode.InjuryDetailInvalid);

        var site = type.AsksSite ? detail.Site ?? type.BaseSite : null;
        if (type.AsksSite && site is null)
            throw new DomainException(ErrorCode.InjurySiteRequired, type.SiteQuestion());
        if (site is not null && !(site.Code == SiteCode && type.Code == Type))
        {
            // El sitio fijo es parte del tipo: si el administrador lo retira, el tipo sigue sirviendo mientras no lo retire también.
            if (!site.Active && site.Code != type.BaseSiteCode)
                throw new DomainException(ErrorCode.InjurySiteRetired, site.Label);
            if (!type.Allows(site))
                throw new DomainException(ErrorCode.InjurySiteInvalid, site.Label, type.Label);
        }

        var sided = InjuryLocation.SideSite(type, site);
        if (sided is { Paired: true } && detail.Side is null)
            throw new DomainException(ErrorCode.InjurySideRequired, BodySite.Lower(sided.Label));

        return new InjuryDetail(site, sided is { Paired: true } ? detail.Side : null, type.Graded ? detail.Grade : null, detail.Mechanism);
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

    // El entrenamiento en que se lesionó se ha borrado (aún no había acabado): la lesión se queda, ya sin saber dónde fue.
    public void ForgetOccasion(DateTimeOffset now)
    {
        SetOccasion(null);
        UpdatedAt = now;
    }

    // Ha cambiado mientras se daba (el título, la hora): se copia lo de ahora. Si ya no cuadra con el parte, se suelta.
    public void RefreshOccasion(InjuryOccasion occasion, DateTimeOffset now)
    {
        if (OccasionId != occasion.Id) return;
        SetOccasion(StartedOn < occasion.Day ? null : occasion);
        UpdatedAt = now;
    }

    private void SetOccasion(InjuryOccasion? occasion)
    {
        MatchId = occasion is { Kind: InjuryOccasionKind.Match } ? occasion.Id : null;
        TrainingSessionId = occasion is { Kind: InjuryOccasionKind.Training } ? occasion.Id : null;
        OccasionDay = occasion?.Day;
        var label = occasion?.Label.Trim();
        OccasionLabel = label is { Length: > InjuryOccasion.LabelMaxLength } ? label[..InjuryOccasion.LabelMaxLength] : label;
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

    // Sin contar la que se hizo en ese mismo partido o entrenamiento (occasionId): hasta lesionarse, jugó o entrenó.
    public PlayerInjury? On(Guid squadPlayerId, DateOnly day, Guid? occasionId = null) =>
        _byPlayer[squadPlayerId].FirstOrDefault(i => i.CoversDay(day) && (occasionId is null || i.OccasionId != occasionId));

    // La que se hizo en ese partido o entrenamiento.
    public PlayerInjury? At(Guid squadPlayerId, Guid occasionId) => _byPlayer[squadPlayerId].FirstOrDefault(i => i.OccasionId == occasionId);

    public IReadOnlyList<PlayerInjury> Of(Guid squadPlayerId) => _byPlayer[squadPlayerId].ToList();
}
