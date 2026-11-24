using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.MatchReports;

namespace TercerTiempo.Domain.Squads;

public enum InjuryProposalStatus
{
    Pending = 1,
    Registered = 2,
    Dismissed = 3,
}

// Una posible lesión de un jugador de la plantilla que cuenta la crónica de un partido. No es una lesión: el club o su
// cuerpo técnico la registran (con el formulario de siempre ya relleno) o la descartan. Lleva copiado lo que dijo la
// crónica, para que siga teniendo sentido aunque la crónica cambie. Una por plantilla, partido y jugador.
public sealed class InjuryProposal : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid MentionId { get; private set; }
    public int? Minute { get; private set; }
    public string Excerpt { get; private set; } = string.Empty;
    public string Phrase { get; private set; } = string.Empty;
    public string SourceUrl { get; private set; } = string.Empty;
    public InjuryProposalStatus Status { get; private set; }
    // La lesión que se registró con ella.
    public Guid? InjuryId { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedByCoachId { get; private set; }
    public Guid? DecidedByClubId { get; private set; }

    public bool IsPending => Status == InjuryProposalStatus.Pending;

    private InjuryProposal() { }

    public static InjuryProposal Create(Guid squadId, Guid squadPlayerId, MatchInjuryMention mention, DateTimeOffset now)
    {
        var proposal = new InjuryProposal
        {
            Id = NewId(),
            SquadId = Guard.NotEmpty(squadId, nameof(squadId)),
            SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId)),
            MatchId = mention.MatchId,
            Status = InjuryProposalStatus.Pending,
            DetectedAt = now,
        };
        proposal.Refresh(mention);
        return proposal;
    }

    // La crónica se ha corregido: mientras nadie la haya decidido, la propuesta dice lo de ahora.
    public bool Refresh(MatchInjuryMention mention)
    {
        if (!IsPending || mention.MatchId != MatchId) return false;
        if (MentionId == mention.Id && Minute == mention.Minute && Excerpt == mention.Excerpt && Phrase == mention.Phrase && SourceUrl == mention.SourceUrl)
            return false;

        MentionId = mention.Id;
        Minute = mention.Minute;
        Excerpt = mention.Excerpt;
        Phrase = mention.Phrase;
        SourceUrl = mention.SourceUrl;
        return true;
    }

    public void Register(Guid injuryId, SquadEditor editor, DateTimeOffset now)
    {
        EnsurePending();
        InjuryId = Guard.NotEmpty(injuryId, nameof(injuryId));
        Decide(InjuryProposalStatus.Registered, editor, now);
    }

    public void Dismiss(SquadEditor editor, DateTimeOffset now)
    {
        EnsurePending();
        Decide(InjuryProposalStatus.Dismissed, editor, now);
    }

    // Se ha borrado la lesión que se registró con ella (se apuntó por error): queda descartada, sin volver a proponerse, y
    // se borra con las demás descartadas al cumplir su plazo.
    public void ForgetInjury(DateTimeOffset now)
    {
        if (Status != InjuryProposalStatus.Registered) return;
        Status = InjuryProposalStatus.Dismissed;
        InjuryId = null;
        DecidedAt = now;
    }

    // Se junta con la otra ficha de la misma persona.
    public void MoveToSquadPlayer(Guid squadPlayerId) => SquadPlayerId = Guard.NotEmpty(squadPlayerId, nameof(squadPlayerId));

    private void Decide(InjuryProposalStatus status, SquadEditor editor, DateTimeOffset now)
    {
        Status = status;
        DecidedAt = now;
        DecidedByCoachId = editor.CoachId;
        DecidedByClubId = editor.ClubId;
    }

    private void EnsurePending()
    {
        if (!IsPending)
            throw new DomainException(ErrorCode.InjuryProposalDecided);
    }
}

public interface IInjuryProposalStore
{
    Task<InjuryProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryProposal>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryProposal>> GetByMatchesAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken = default);
    // Las pendientes y las descartadas detectadas antes de ese momento (las registradas se quedan con su lesión).
    Task<IReadOnlyList<InjuryProposal>> GetExpiredAsync(DateTimeOffset detectedBefore, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InjuryProposal>> GetPendingAsync(CancellationToken cancellationToken = default);
    void Add(InjuryProposal proposal);
    void Remove(InjuryProposal proposal);
}
