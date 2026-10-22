using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams.ImportCrest;
using TercerTiempo.Application.Teams.SyncProfiles;

namespace TercerTiempo.Repository.Admin;

internal sealed class AdminRepository(
    ICommandHandler<SyncSeasonCommand, SyncResultDto> syncSeason,
    IQueryHandler<GetSyncStatusQuery, SyncStatusDto> syncStatus,
    ICommandHandler<SyncTeamProfilesCommand, TeamProfileSyncResultDto> syncProfiles,
    ICommandHandler<ImportTeamCrestCommand, TeamCrestImportDto> importCrest) : IAdminRepository
{
    public Task<SyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default) =>
        syncStatus.Handle(new GetSyncStatusQuery(), cancellationToken);

    public Task<SyncResultDto> SyncSeasonAsync(CancellationToken cancellationToken = default) =>
        syncSeason.Handle(new SyncSeasonCommand(), cancellationToken);

    public Task<TeamProfileSyncResultDto> SyncTeamProfilesAsync(bool force, CancellationToken cancellationToken = default) =>
        syncProfiles.Handle(new SyncTeamProfilesCommand(force), cancellationToken);

    public Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default) =>
        importCrest.Handle(new ImportTeamCrestCommand(teamId, url), cancellationToken);
}

public sealed record ImportCrestRequest(string Url);
