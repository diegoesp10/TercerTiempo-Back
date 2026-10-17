using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Players;

public sealed record PlayerRegisteredEvent(Guid PlayerId, DateTimeOffset OccurredAt) : IDomainEvent;
