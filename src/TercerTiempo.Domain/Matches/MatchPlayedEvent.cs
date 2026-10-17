using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Matches;

public sealed record MatchPlayedEvent(Guid MatchId, DateTimeOffset OccurredAt) : IDomainEvent;
