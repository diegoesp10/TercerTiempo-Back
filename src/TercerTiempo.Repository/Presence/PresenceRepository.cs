using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Presence;

namespace TercerTiempo.Repository.Presence;

public interface IPresenceRepository
{
    Task<bool> RecordAsync(string? path, string? userAgent, CancellationToken cancellationToken = default);
    Task<bool> EndAsync(CancellationToken cancellationToken = default);
}

internal sealed class PresenceRepository(
    ICommandHandler<RecordPresenceCommand, bool> record,
    ICommandHandler<EndPresenceCommand, bool> end) : IPresenceRepository
{
    public Task<bool> RecordAsync(string? path, string? userAgent, CancellationToken cancellationToken = default) =>
        record.Handle(new RecordPresenceCommand(path, userAgent), cancellationToken);

    public Task<bool> EndAsync(CancellationToken cancellationToken = default) =>
        end.Handle(new EndPresenceCommand(), cancellationToken);
}
