using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// La convocatoria de un partido: a quién lleva el cuerpo técnico, dónde y a qué hora se queda. En borrador solo la ve
// el cuerpo técnico; al enviarla (publicarla) se avisa a toda la plantilla (al que va y al que no) y cada jugador la
// firma desde la app: el convocado firma que va, el que no va se da por enterado. Antes de la firma, el cuerpo técnico
// ve a quién le ha llegado (NotifiedAt) y quién la ha abierto (SeenAt), como el doble check de un mensaje.
public sealed class MatchCallUp : AggregateRoot
{
    private readonly List<CallUpEntry> _entries = [];

    public Guid SquadId { get; private set; }
    public Guid MatchId { get; private set; }
    public CallUpStatus Status { get; private set; }
    // A cuántos se convoca: el número fijo de la plantilla o el que se diga en esta convocatoria. Es exacto: nunca se
    // pasa y, si se envía con menos, se avisa de que falta gente. Las de antes de que existiera el número no lo tienen.
    public int? Size { get; private set; }
    public DateTimeOffset? MeetAt { get; private set; }
    // Dónde se cita: un campo de las ligas o, si no está en la lista (obras, otro sitio), lo que se escriba.
    public Guid? MeetGroundId { get; private set; }
    public string? MeetPlace { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedByCoachId { get; private set; }
    public Guid? CreatedByClubId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyCollection<CallUpEntry> Entries => _entries.AsReadOnly();

    public bool IsPublished => Status == CallUpStatus.Published;

    public IEnumerable<CallUpEntry> Called => _entries.Where(e => e.Called);

    private MatchCallUp() { }

    public static MatchCallUp Draft(Squad squad, Match match, SquadEditor editor, DateTimeOffset now)
    {
        if (match.IsOver(now) || match.KickoffAt <= now)
            throw new DomainException(ErrorCode.CallUpMatchPlayed);

        var callUp = new MatchCallUp
        {
            Id = NewId(),
            SquadId = squad.Id,
            MatchId = match.Id,
            Status = CallUpStatus.Draft,
            Size = squad.CallUpSize,
            CreatedByCoachId = editor.CoachId,
            CreatedByClubId = editor.ClubId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var player in squad.ActivePlayers)
            callUp._entries.Add(CallUpEntry.Create(callUp.Id, player.Id));
        return callUp;
    }

    // Qué jugadores van y los detalles de la cita. Devuelve a quién le cambia la convocatoria respecto a lo que ya se
    // había publicado, para avisarle. El número fijo de la plantilla manda; si no lo tiene, vale el que se diga ahora
    // (o el que ya tenía la convocatoria).
    public IReadOnlyList<CallUpEntry> Arrange(
        Squad squad, int? size, IReadOnlySet<Guid> calledSquadPlayerIds, DateTimeOffset? meetAt, Guid? meetGroundId, string? meetPlace, string? notes, DateTimeOffset now)
    {
        var target = squad.CallUpSize ?? size ?? Size ?? throw new DomainException(ErrorCode.CallUpSizeRequired);
        if (!Squad.IsValidCallUpSize(target))
            throw new DomainException(ErrorCode.CallUpSizeInvalid, Squad.MinCallUpSize, Squad.MaxCallUpSize);

        var calling = squad.ActivePlayers.Count(p => calledSquadPlayerIds.Contains(p.Id));
        if (calling > target)
            throw new DomainException(ErrorCode.CallUpOverSize, target, calling, calling - target);
        Size = target;

        foreach (var player in squad.ActivePlayers)
        {
            if (_entries.All(e => e.SquadPlayerId != player.Id))
                _entries.Add(CallUpEntry.Create(Id, player.Id));
        }

        var changed = new List<CallUpEntry>();
        foreach (var entry in _entries)
        {
            var called = calledSquadPlayerIds.Contains(entry.SquadPlayerId);
            if (entry.SetCalled(called, now)) changed.Add(entry);
        }

        var ground = meetGroundId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var place = ground is not null || string.IsNullOrWhiteSpace(meetPlace) ? null : Guard.NotEmpty(meetPlace, "lugar", 150);
        // Ya enviada, si cambia la hora o el sitio de la cita, los convocados tienen que enterarse: se les vuelve a avisar
        // y el doble check vuelve a gris hasta que la abran otra vez. La firma se mantiene: siguen convocados.
        if (IsPublished && (meetAt != MeetAt || ground != MeetGroundId || place != MeetPlace))
        {
            foreach (var entry in _entries.Where(e => e.Called && !changed.Contains(e)))
            {
                entry.MeetingChanged();
                changed.Add(entry);
            }
        }

        MeetAt = meetAt;
        MeetGroundId = ground;
        MeetPlace = place;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.NotEmpty(notes, "notas", 1000);
        UpdatedAt = now;
        return changed;
    }

    // Cuántos faltan para llegar al número, contando solo a quien sigue en la plantilla. Sin número, ninguno.
    public int Missing(Squad squad)
    {
        if (Size is not { } size) return 0;
        var active = squad.ActivePlayers.Select(p => p.Id).ToHashSet();
        return Math.Max(0, size - _entries.Count(e => e.Called && active.Contains(e.SquadPlayerId)));
    }

    public void Publish(DateTimeOffset now)
    {
        if (!_entries.Any(e => e.Called))
            throw new DomainException(ErrorCode.CallUpNoPlayers);

        Status = CallUpStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    public CallUpEntry Respond(Guid squadPlayerId, CallUpResponse response, Match match, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException(ErrorCode.CallUpNotPublished);
        if (match.KickoffAt <= now)
            throw new DomainException(ErrorCode.CallUpMatchStarted);
        if (!Enum.IsDefined(response) || response == CallUpResponse.Pending)
            throw new DomainException(ErrorCode.CallUpInvalidResponse);

        var entry = _entries.FirstOrDefault(e => e.SquadPlayerId == squadPlayerId)
            ?? throw new DomainException(ErrorCode.SquadPlayerNotMember);

        if (entry.Called && response != CallUpResponse.Confirmed)
            throw new DomainException(ErrorCode.CallUpInvalidResponse);
        if (!entry.Called && response != CallUpResponse.Acknowledged)
            throw new DomainException(ErrorCode.CallUpNotCalled);

        entry.Respond(response, now);
        UpdatedAt = now;
        return entry;
    }

    public CallUpEntry? EntryOf(Guid squadPlayerId) => _entries.FirstOrDefault(e => e.SquadPlayerId == squadPlayerId);

    // El jugador abre la convocatoria enviada. Abrirla otra vez no cambia la hora de la primera.
    public CallUpEntry MarkSeen(Guid squadPlayerId, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException(ErrorCode.CallUpNotPublished);

        var entry = EntryOf(squadPlayerId)
            ?? throw new DomainException(ErrorCode.SquadPlayerNotMember);
        entry.MarkSeen(now);
        return entry;
    }

    // Dos fichas de la misma persona que se juntan: queda una línea, la de la que se queda, con lo que diga más de las dos
    // (convocado, firmado o visto). Nunca cambia de ficha una línea: así no choca con la que ya tenía la otra.
    public void MergeEntries(Guid fromSquadPlayerId, Guid toSquadPlayerId)
    {
        var from = EntryOf(fromSquadPlayerId);
        if (from is null) return;

        var to = EntryOf(toSquadPlayerId);
        if (to is null)
            _entries.Add(CallUpEntry.Create(Id, toSquadPlayerId).TakeOver(from));
        else if (Weight(from) > Weight(to))
            to.TakeOver(from);
        _entries.Remove(from);
    }

    private static int Weight(CallUpEntry entry) =>
        (entry.Called ? 4 : 0) + (entry.Response != CallUpResponse.Pending ? 2 : 0) + (entry.SeenAt is not null ? 1 : 0);

    // A quién se le puede volver a enviar: ya enviada y sin abrir.
    public IEnumerable<CallUpEntry> UnseenEntries() => IsPublished ? _entries.Where(e => e.SeenAt is null) : [];
}

public sealed class CallUpEntry : Entity
{
    public Guid CallUpId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    public bool Called { get; private set; }
    public CallUpResponse Response { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public DateTimeOffset? NotifiedAt { get; private set; }
    public DateTimeOffset? SeenAt { get; private set; }

    private CallUpEntry() { }

    internal static CallUpEntry Create(Guid callUpId, Guid squadPlayerId) => new()
    {
        Id = NewId(),
        CallUpId = callUpId,
        SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId)),
        Called = false,
        Response = CallUpResponse.Pending,
    };

    internal CallUpEntry TakeOver(CallUpEntry other)
    {
        Called = other.Called;
        Response = other.Response;
        RespondedAt = other.RespondedAt;
        NotifiedAt = other.NotifiedAt;
        SeenAt = other.SeenAt;
        return this;
    }

    // true si cambia: entonces la firma anterior deja de valer y hay que volver a avisar al jugador.
    internal bool SetCalled(bool called, DateTimeOffset now)
    {
        if (Called == called) return false;
        Called = called;
        Response = CallUpResponse.Pending;
        RespondedAt = null;
        NotifiedAt = null;
        SeenAt = null;
        return true;
    }

    internal void Respond(CallUpResponse response, DateTimeOffset now)
    {
        Response = response;
        RespondedAt = now;
        SeenAt ??= now;
    }

    internal void MarkSeen(DateTimeOffset now) => SeenAt ??= now;

    internal void MeetingChanged()
    {
        NotifiedAt = null;
        SeenAt = null;
    }

    public void MarkNotified(DateTimeOffset now) => NotifiedAt = now;
}

public interface ICallUpStore
{
    Task<MatchCallUp?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MatchCallUp?> GetByMatchAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchCallUp>> GetPublishedBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);

    // Todas, borradores incluidos: solo para quien las prepara.
    Task<IReadOnlyList<MatchCallUp>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);

    // Las de esos partidos: todas o solo las publicadas.
    Task<IReadOnlyList<MatchCallUp>> GetBySquadAndMatchesAsync(
        Guid squadId, IEnumerable<Guid> matchIds, bool publishedOnly, CancellationToken cancellationToken = default);

    // El número de la última convocatoria que lo tiene: lo que se propone al preguntar a cuántos se convoca.
    Task<int?> GetLatestSizeAsync(Guid squadId, CancellationToken cancellationToken = default);

    // Solo lo que entra en la huella de la convocatoria publicada (null si no la hay o es un borrador), sin cargarla.
    Task<CallUpStampRow?> GetStampRowAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default);
    void Add(MatchCallUp callUp);
}

public sealed record CallUpStampRow(Guid Id, DateTimeOffset UpdatedAt, IReadOnlyList<CallUpStampLine> Lines);

public sealed record CallUpStampLine(Guid SquadPlayerId, bool Called, CallUpResponse Response, DateTimeOffset? NotifiedAt, DateTimeOffset? SeenAt);
