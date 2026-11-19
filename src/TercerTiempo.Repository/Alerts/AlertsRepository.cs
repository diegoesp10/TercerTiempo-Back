using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Alerts;

namespace TercerTiempo.Repository.Alerts;

public interface IAlertsRepository
{
    Task<ClubAlertsDto> GetMineAsync(CancellationToken cancellationToken = default);
    Task<ClubAlertsDto> MarkReadAsync(Guid alertId, CancellationToken cancellationToken = default);
    Task<ClubAlertsDto> MarkAllReadAsync(CancellationToken cancellationToken = default);
    Task<ClubAlertsDto> MarkSeenAsync(IReadOnlyList<Guid> alertIds, CancellationToken cancellationToken = default);
}

internal sealed class AlertsRepository(
    IQueryHandler<GetClubAlertsQuery, ClubAlertsDto> getAlerts,
    ICommandHandler<MarkAlertReadCommand, ClubAlertsDto> markRead,
    ICommandHandler<MarkAllAlertsReadCommand, ClubAlertsDto> markAllRead,
    ICommandHandler<MarkAlertsSeenCommand, ClubAlertsDto> markSeen) : IAlertsRepository
{
    public Task<ClubAlertsDto> GetMineAsync(CancellationToken cancellationToken = default) =>
        getAlerts.Handle(new GetClubAlertsQuery(), cancellationToken);

    public Task<ClubAlertsDto> MarkReadAsync(Guid alertId, CancellationToken cancellationToken = default) =>
        markRead.Handle(new MarkAlertReadCommand(alertId), cancellationToken);

    public Task<ClubAlertsDto> MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        markAllRead.Handle(new MarkAllAlertsReadCommand(), cancellationToken);

    public Task<ClubAlertsDto> MarkSeenAsync(IReadOnlyList<Guid> alertIds, CancellationToken cancellationToken = default) =>
        markSeen.Handle(new MarkAlertsSeenCommand(alertIds), cancellationToken);
}
