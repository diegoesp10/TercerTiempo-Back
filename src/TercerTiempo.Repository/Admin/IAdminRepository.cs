using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Clubs.Invitations;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Teams.ImportCrest;

namespace TercerTiempo.Repository.Admin;

public interface IAdminRepository
{
    Task<AdminAuthResponse> LoginAsync(AdminLoginCommand command, CancellationToken cancellationToken = default);
    Task<JobsOverviewDto> GetJobsAsync(string? provider, int? requests, CancellationToken cancellationToken = default);
    Task<JobDto> RunJobAsync(string job, CancellationToken cancellationToken = default);
    Task<LicensesOverviewDto> GetLicensesAsync(CancellationToken cancellationToken = default);
    Task<LicensingSettingsDto> GetLicensingSettingsAsync(CancellationToken cancellationToken = default);
    Task<FootballSyncSettingsDto> GetFootballSyncSettingsAsync(CancellationToken cancellationToken = default);
    Task<FootballSyncSettingsDto> UpdateFootballSyncSettingsAsync(UpdateFootballSyncSettingsCommand command, CancellationToken cancellationToken = default);
    Task<JobDto> RunFootballSyncAsync(RunFootballSyncCommand command, CancellationToken cancellationToken = default);
    Task<LicensingSettingsDto> UpdateLicensingSettingsAsync(UpdateLicensingSettingsCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubInvitationDto>> GetClubInvitationsAsync(CancellationToken cancellationToken = default);
    Task<ClubInvitationDto> CreateClubInvitationAsync(CreateClubInvitationCommand command, CancellationToken cancellationToken = default);
    Task<ClubInvitationDto> RevokeClubInvitationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default);
}
