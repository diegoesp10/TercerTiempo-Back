using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Clubs.Invitations;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Teams.ImportCrest;

namespace TercerTiempo.Repository.Admin;

internal sealed class AdminRepository(
    ICommandHandler<AdminLoginCommand, AdminAuthResponse> login,
    IQueryHandler<GetJobsOverviewQuery, JobsOverviewDto> jobs,
    ICommandHandler<RunJobCommand, JobDto> runJob,
    IQueryHandler<GetFootballSyncSettingsQuery, FootballSyncSettingsDto> footballSyncSettings,
    ICommandHandler<UpdateFootballSyncSettingsCommand, FootballSyncSettingsDto> updateFootballSyncSettings,
    ICommandHandler<RunFootballSyncCommand, JobDto> runFootballSync,
    IQueryHandler<GetLicensesOverviewQuery, LicensesOverviewDto> licenses,
    IQueryHandler<GetLicensingSettingsQuery, LicensingSettingsDto> licensingSettings,
    ICommandHandler<UpdateLicensingSettingsCommand, LicensingSettingsDto> updateLicensingSettings,
    IQueryHandler<ListClubInvitationsQuery, IReadOnlyList<ClubInvitationDto>> listInvitations,
    ICommandHandler<CreateClubInvitationCommand, ClubInvitationDto> createInvitation,
    ICommandHandler<RevokeClubInvitationCommand, ClubInvitationDto> revokeInvitation,
    ICommandHandler<ImportTeamCrestCommand, TeamCrestImportDto> importCrest) : IAdminRepository
{
    public Task<AdminAuthResponse> LoginAsync(AdminLoginCommand command, CancellationToken cancellationToken = default) =>
        login.Handle(command, cancellationToken);

    public Task<JobsOverviewDto> GetJobsAsync(string? provider, int? requests, CancellationToken cancellationToken = default) =>
        jobs.Handle(new GetJobsOverviewQuery(provider, requests ?? 200), cancellationToken);

    public Task<JobDto> RunJobAsync(string job, CancellationToken cancellationToken = default) =>
        runJob.Handle(new RunJobCommand(job), cancellationToken);

    public Task<FootballSyncSettingsDto> GetFootballSyncSettingsAsync(CancellationToken cancellationToken = default) =>
        footballSyncSettings.Handle(new GetFootballSyncSettingsQuery(), cancellationToken);

    public Task<FootballSyncSettingsDto> UpdateFootballSyncSettingsAsync(UpdateFootballSyncSettingsCommand command, CancellationToken cancellationToken = default) =>
        updateFootballSyncSettings.Handle(command, cancellationToken);

    public Task<JobDto> RunFootballSyncAsync(RunFootballSyncCommand command, CancellationToken cancellationToken = default) =>
        runFootballSync.Handle(command, cancellationToken);

    public Task<LicensesOverviewDto> GetLicensesAsync(CancellationToken cancellationToken = default) =>
        licenses.Handle(new GetLicensesOverviewQuery(), cancellationToken);

    public Task<LicensingSettingsDto> GetLicensingSettingsAsync(CancellationToken cancellationToken = default) =>
        licensingSettings.Handle(new GetLicensingSettingsQuery(), cancellationToken);

    public Task<LicensingSettingsDto> UpdateLicensingSettingsAsync(UpdateLicensingSettingsCommand command, CancellationToken cancellationToken = default) =>
        updateLicensingSettings.Handle(command, cancellationToken);

    public Task<IReadOnlyList<ClubInvitationDto>> GetClubInvitationsAsync(CancellationToken cancellationToken = default) =>
        listInvitations.Handle(new ListClubInvitationsQuery(), cancellationToken);

    public Task<ClubInvitationDto> CreateClubInvitationAsync(CreateClubInvitationCommand command, CancellationToken cancellationToken = default) =>
        createInvitation.Handle(command, cancellationToken);

    public Task<ClubInvitationDto> RevokeClubInvitationAsync(Guid id, CancellationToken cancellationToken = default) =>
        revokeInvitation.Handle(new RevokeClubInvitationCommand(id), cancellationToken);

    public Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default) =>
        importCrest.Handle(new ImportTeamCrestCommand(teamId, url), cancellationToken);
}

public sealed record ImportCrestRequest(string Url);
