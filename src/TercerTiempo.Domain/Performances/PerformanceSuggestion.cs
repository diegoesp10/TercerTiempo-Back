using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Performances;

public enum SuggestionStatus
{
    Pending = 1,
    Approved = 2,
    Declined = 3,
}

// Un jugador vinculado propone su línea del acta de un partido (corregirla o crearla si no existe). La decide el
// club propietario o el cuerpo técnico: al aprobarla se escribe en el acta en nombre del cuerpo técnico.
public sealed class PerformanceSuggestion : AggregateRoot
{
    public const int MaxMessageLength = 500;

    public Guid SquadId { get; private set; }
    public Guid SquadPlayerId { get; private set; }
    public Guid PlayerId { get; private set; }
    public Guid MatchId { get; private set; }

    public bool Started { get; private set; }
    public int MinutesPlayed { get; private set; }
    public int Goals { get; private set; }
    public int Assists { get; private set; }
    public int Shots { get; private set; }
    public int ShotsOnTarget { get; private set; }
    public int KeyPasses { get; private set; }
    public int Tackles { get; private set; }
    public int Interceptions { get; private set; }
    public int Saves { get; private set; }
    public int YellowCards { get; private set; }
    public bool RedCard { get; private set; }

    public string? Message { get; private set; }
    public SuggestionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedByCoachId { get; private set; }
    public Guid? ResolvedByClubId { get; private set; }
    public string? ResolutionNote { get; private set; }

    public bool IsPending => Status == SuggestionStatus.Pending;

    private PerformanceSuggestion() { }

    public static PerformanceSuggestion Create(Squad squad, SquadPlayer entry, Guid playerId, Match match, PerformanceStats stats, string? message, DateTimeOffset now)
    {
        if (entry.SquadId != squad.Id || entry.PlayerId != playerId)
            throw new DomainException(ErrorCode.PlayerNotInSquad);

        if (match.SeasonId != squad.SeasonId)
            throw new DomainException(ErrorCode.SheetMatchOtherSeason);

        if (!match.Involves(squad.TeamId))
            throw new DomainException(ErrorCode.SheetTeamNotInvolved);

        if (match.Status != MatchStatus.Played)
            throw new DomainException(ErrorCode.SheetMatchNotPlayed);

        stats = stats with { Notes = null };
        stats.EnsureValid();

        if (message is { Length: > MaxMessageLength })
            throw new DomainException(ErrorCode.SuggestionMessageTooLong, MaxMessageLength);

        return new PerformanceSuggestion
        {
            Id = NewId(),
            SquadId = squad.Id,
            SquadPlayerId = entry.Id,
            PlayerId = playerId,
            MatchId = match.Id,
            Started = stats.Started,
            MinutesPlayed = stats.MinutesPlayed,
            Goals = stats.Goals,
            Assists = stats.Assists,
            Shots = stats.Shots,
            ShotsOnTarget = stats.ShotsOnTarget,
            KeyPasses = stats.KeyPasses,
            Tackles = stats.Tackles,
            Interceptions = stats.Interceptions,
            Saves = stats.Saves,
            YellowCards = stats.YellowCards,
            RedCard = stats.RedCard,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            Status = SuggestionStatus.Pending,
            CreatedAt = now,
        };
    }

    public PerformanceStats ToStats(string? notes = null) => new(
        Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, notes);

    public void Approve(Guid? coachId, Guid? clubId, string? note, DateTimeOffset now) => Resolve(SuggestionStatus.Approved, coachId, clubId, note, now);

    public void Decline(Guid? coachId, Guid? clubId, string? note, DateTimeOffset now) => Resolve(SuggestionStatus.Declined, coachId, clubId, note, now);

    private void Resolve(SuggestionStatus status, Guid? coachId, Guid? clubId, string? note, DateTimeOffset now)
    {
        if (!IsPending)
            throw new DomainException(ErrorCode.SuggestionAlreadyResolved);

        if (coachId is null && clubId is null)
            throw new DomainException(ErrorCode.SquadManagersOnly);

        if (note is { Length: > MaxMessageLength })
            throw new DomainException(ErrorCode.SuggestionMessageTooLong, MaxMessageLength);

        Status = status;
        ResolvedAt = now;
        ResolvedByCoachId = coachId;
        ResolvedByClubId = clubId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

public interface IPerformanceSuggestionStore
{
    Task<PerformanceSuggestion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceSuggestion>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceSuggestion>> GetByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);
    void Add(PerformanceSuggestion suggestion);
}
