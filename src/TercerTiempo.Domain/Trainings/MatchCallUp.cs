using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// La convocatoria de un partido: a quién lleva el cuerpo técnico, dónde y a qué hora se queda. En borrador solo la ve
// el cuerpo técnico; al publicarla se avisa a toda la plantilla (al que va y al que no) y cada jugador la firma desde la
// app: el convocado confirma o dice que no puede, el que no va se da por enterado.
public sealed class MatchCallUp : AggregateRoot
{
    private readonly List<CallUpEntry> _entries = [];

    public Guid SquadId { get; private set; }
    public Guid MatchId { get; private set; }
    public CallUpStatus Status { get; private set; }
    public DateTimeOffset? MeetAt { get; private set; }
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
    // había publicado, para avisarle.
    public IReadOnlyList<CallUpEntry> Arrange(Squad squad, IReadOnlySet<Guid> calledSquadPlayerIds, DateTimeOffset? meetAt, string? meetPlace, string? notes, DateTimeOffset now)
    {
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

        MeetAt = meetAt;
        MeetPlace = string.IsNullOrWhiteSpace(meetPlace) ? null : Guard.NotEmpty(meetPlace, "lugar", 150);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : Guard.NotEmpty(notes, "notas", 1000);
        UpdatedAt = now;
        return changed;
    }

    public void Publish(DateTimeOffset now)
    {
        if (!_entries.Any(e => e.Called))
            throw new DomainException(ErrorCode.CallUpNoPlayers);

        Status = CallUpStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    public CallUpEntry Respond(Guid squadPlayerId, CallUpResponse response, string? note, Match match, DateTimeOffset now)
    {
        if (!IsPublished)
            throw new DomainException(ErrorCode.CallUpNotPublished);
        if (match.KickoffAt <= now)
            throw new DomainException(ErrorCode.CallUpMatchStarted);
        if (!Enum.IsDefined(response) || response == CallUpResponse.Pending)
            throw new DomainException(ErrorCode.CallUpInvalidResponse);

        var entry = _entries.FirstOrDefault(e => e.SquadPlayerId == squadPlayerId)
            ?? throw new DomainException(ErrorCode.SquadPlayerNotMember);

        if (entry.Called && response == CallUpResponse.Acknowledged)
            throw new DomainException(ErrorCode.CallUpInvalidResponse);
        if (!entry.Called && response != CallUpResponse.Acknowledged)
            throw new DomainException(ErrorCode.CallUpNotCalled);

        entry.Respond(response, note, now);
        UpdatedAt = now;
        return entry;
    }

    public CallUpEntry? EntryOf(Guid squadPlayerId) => _entries.FirstOrDefault(e => e.SquadPlayerId == squadPlayerId);
}

public sealed class CallUpEntry : Entity
{
    public Guid CallUpId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    public bool Called { get; private set; }
    public CallUpResponse Response { get; private set; }
    public string? ResponseNote { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public DateTimeOffset? NotifiedAt { get; private set; }

    private CallUpEntry() { }

    internal static CallUpEntry Create(Guid callUpId, Guid squadPlayerId) => new()
    {
        Id = NewId(),
        CallUpId = callUpId,
        SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId)),
        Called = false,
        Response = CallUpResponse.Pending,
    };

    // true si cambia: entonces la respuesta anterior deja de valer y hay que volver a avisar al jugador.
    internal bool SetCalled(bool called, DateTimeOffset now)
    {
        if (Called == called) return false;
        Called = called;
        Response = CallUpResponse.Pending;
        ResponseNote = null;
        RespondedAt = null;
        NotifiedAt = null;
        return true;
    }

    internal void Respond(CallUpResponse response, string? note, DateTimeOffset now)
    {
        Response = response;
        ResponseNote = string.IsNullOrWhiteSpace(note) ? null : Guard.NotEmpty(note, "nota", 300);
        RespondedAt = now;
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
    void Add(MatchCallUp callUp);
}
