using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Teams.ImportCrest;

namespace TercerTiempo.Repository.Admin;

internal sealed class AdminRepository(
    ICommandHandler<AdminLoginCommand, AdminAuthResponse> login,
    IQueryHandler<GetJobsOverviewQuery, JobsOverviewDto> jobs,
    ICommandHandler<RunJobCommand, JobDto> runJob,
    ICommandHandler<ImportTeamCrestCommand, TeamCrestImportDto> importCrest) : IAdminRepository
{
    public Task<AdminAuthResponse> LoginAsync(AdminLoginCommand command, CancellationToken cancellationToken = default) =>
        login.Handle(command, cancellationToken);

    public Task<JobsOverviewDto> GetJobsAsync(string? provider, int? requests, CancellationToken cancellationToken = default) =>
        jobs.Handle(new GetJobsOverviewQuery(provider, requests ?? 200), cancellationToken);

    public Task<JobDto> RunJobAsync(string job, CancellationToken cancellationToken = default) =>
        runJob.Handle(new RunJobCommand(job), cancellationToken);

    public Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default) =>
        importCrest.Handle(new ImportTeamCrestCommand(teamId, url), cancellationToken);
}

public sealed record ImportCrestRequest(string Url);
