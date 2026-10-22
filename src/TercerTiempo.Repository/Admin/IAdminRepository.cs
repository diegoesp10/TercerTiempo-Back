using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams.ImportCrest;
using TercerTiempo.Application.Teams.SyncProfiles;

namespace TercerTiempo.Repository.Admin;

public interface IAdminRepository
{
    Task<SyncResultDto> SyncSeasonAsync(CancellationToken cancellationToken = default);
    Task<SyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default);
    Task<TeamProfileSyncResultDto> SyncTeamProfilesAsync(bool force, CancellationToken cancellationToken = default);
    Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default);
}
