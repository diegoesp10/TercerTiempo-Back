using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Teams.ImportCrest;

namespace TercerTiempo.Repository.Admin;

public interface IAdminRepository
{
    Task<AdminAuthResponse> LoginAsync(AdminLoginCommand command, CancellationToken cancellationToken = default);
    Task<JobsOverviewDto> GetJobsAsync(string? provider, int? requests, CancellationToken cancellationToken = default);
    Task<JobDto> RunJobAsync(string job, CancellationToken cancellationToken = default);
    Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default);
}
