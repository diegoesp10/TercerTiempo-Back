using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Sync;

namespace TercerTiempo.Repository.Admin;

internal sealed class AdminRepository(ICommandHandler<SyncSeasonCommand, SyncResultDto> syncSeason) : IAdminRepository
{
    public Task<SyncResultDto> SyncSeasonAsync(CancellationToken cancellationToken = default) =>
        syncSeason.Handle(new SyncSeasonCommand(), cancellationToken);
}
