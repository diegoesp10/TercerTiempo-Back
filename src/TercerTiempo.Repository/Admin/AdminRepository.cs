using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Clubs.Invitations;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Application.Teams.ImportCrest;
using TercerTiempo.Application.Abstractions.RealData;
using Microsoft.Extensions.DependencyInjection;

namespace TercerTiempo.Repository.Admin;

internal sealed class AdminRepository(
    ICommandHandler<AdminLoginCommand, AdminAuthResponse> login,
    ICommandHandler<AdminVerifyMfaCommand, AdminAuthResponse> verifyMfa,
    IQueryHandler<GetJobsOverviewQuery, JobsOverviewDto> jobs,
    ICommandHandler<RunJobCommand, JobDto> runJob,
    IQueryHandler<TercerTiempo.Application.Privacy.GetAnalyticsReportQuery, TercerTiempo.Application.Privacy.AnalyticsReportDto> analytics,
    IQueryHandler<TercerTiempo.Application.Privacy.GetAdminCookiePolicyQuery, TercerTiempo.Application.Privacy.AdminCookiePolicyDto> cookiePolicy,
    ICommandHandler<TercerTiempo.Application.Privacy.UpsertCookieCommand, TercerTiempo.Application.Privacy.AdminCookiePolicyDto> upsertCookie,
    ICommandHandler<TercerTiempo.Application.Privacy.DeleteCookieCommand, TercerTiempo.Application.Privacy.AdminCookiePolicyDto> deleteCookie,
    ICommandHandler<TercerTiempo.Application.Privacy.PublishCookiePolicyCommand, TercerTiempo.Application.Privacy.AdminCookiePolicyDto> publishCookies,
    IQueryHandler<TercerTiempo.Application.Squads.Injuries.ListAdminInjuryTypesQuery, IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminInjuryTypeDto>> injuryTypes,
    ICommandHandler<TercerTiempo.Application.Squads.Injuries.SaveInjuryTypeCommand, IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminInjuryTypeDto>> saveInjuryType,
    ICommandHandler<TercerTiempo.Application.Squads.Injuries.DeleteInjuryTypeCommand, IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminInjuryTypeDto>> deleteInjuryType,
    IQueryHandler<TercerTiempo.Application.Squads.Injuries.ListAdminBodySitesQuery, IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminBodySiteDto>> bodySites,
    ICommandHandler<TercerTiempo.Application.Squads.Injuries.SaveBodySiteCommand, IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminBodySiteDto>> saveBodySite,
    ICommandHandler<TercerTiempo.Application.Squads.Injuries.DeleteBodySiteCommand, IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminBodySiteDto>> deleteBodySite,
    IRealData realData,
    IQueryHandler<TercerTiempo.Application.Presence.GetPresenceQuery, TercerTiempo.Application.Presence.PresenceOverviewDto> presence,
    IQueryHandler<TercerTiempo.Application.Licensing.ListPaymentEventsQuery, IReadOnlyList<TercerTiempo.Application.Licensing.PaymentEventDto>> paymentEvents,
    ICommandHandler<TercerTiempo.Application.Licensing.RetryPaymentEventCommand, bool> retryPaymentEvent,
    ICommandHandler<TercerTiempo.Application.Licensing.SimulateLicenseEventCommand, string> simulateLicenseEvent,
    IQueryHandler<GetLicensesOverviewQuery, LicensesOverviewDto> licenses,
    IQueryHandler<GetLicensingSettingsQuery, LicensingSettingsDto> licensingSettings,
    ICommandHandler<UpdateLicensingSettingsCommand, LicensingSettingsDto> updateLicensingSettings,
    IQueryHandler<ListClubInvitationsQuery, IReadOnlyList<ClubInvitationDto>> listInvitations,
    IQueryHandler<SearchInvitationClubsQuery, IReadOnlyList<InvitationClubDto>> searchInvitationClubs,
    ICommandHandler<CreateClubInvitationCommand, ClubInvitationDto> createInvitation,
    ICommandHandler<RevokeClubInvitationCommand, ClubInvitationDto> revokeInvitation) : IAdminRepository
{
    public Task<AdminAuthResponse> LoginAsync(AdminLoginCommand command, CancellationToken cancellationToken = default) =>
        login.Handle(command, cancellationToken);

    public Task<AdminAuthResponse> VerifyMfaAsync(AdminVerifyMfaCommand command, CancellationToken cancellationToken = default) =>
        verifyMfa.Handle(command, cancellationToken);

    public Task<JobsOverviewDto> GetJobsAsync(string? provider, int? requests, CancellationToken cancellationToken = default) =>
        jobs.Handle(new GetJobsOverviewQuery(provider, requests ?? 200), cancellationToken);

    public Task<JobDto> RunJobAsync(string job, CancellationToken cancellationToken = default) =>
        runJob.Handle(new RunJobCommand(job), cancellationToken);

    public Task<TercerTiempo.Application.Presence.PresenceOverviewDto> GetPresenceAsync(string? query, CancellationToken cancellationToken = default) =>
        presence.Handle(new TercerTiempo.Application.Presence.GetPresenceQuery(query), cancellationToken);

    // Los ajustes de la pasada de las ligas, la cola de actas, la temporada en curso y los escudos son datos reales: van a
    // la base compartida (IRealData) y lo que cambia se copia enseguida a la de este entorno.
    public Task<FootballSyncSettingsDto> GetFootballSyncSettingsAsync(CancellationToken cancellationToken = default) =>
        realData.RunAsync(sp => sp.GetRequiredService<IQueryHandler<GetFootballSyncSettingsQuery, FootballSyncSettingsDto>>()
            .Handle(new GetFootballSyncSettingsQuery(), cancellationToken));

    public Task<FootballSyncSettingsDto> UpdateFootballSyncSettingsAsync(UpdateFootballSyncSettingsCommand command, CancellationToken cancellationToken = default) =>
        realData.RunAsync(sp => sp.GetRequiredService<ICommandHandler<UpdateFootballSyncSettingsCommand, FootballSyncSettingsDto>>()
            .Handle(command, cancellationToken));

    public Task<JobDto> RunFootballSyncAsync(RunFootballSyncCommand command, CancellationToken cancellationToken = default) =>
        realData.RunAsync(sp => sp.GetRequiredService<ICommandHandler<RunFootballSyncCommand, JobDto>>().Handle(command, cancellationToken));

    public Task<LiveDownloadsDto> GetLiveDownloadsAsync(CancellationToken cancellationToken = default) =>
        realData.RunAsync(sp => sp.GetRequiredService<IQueryHandler<GetLiveDownloadsQuery, LiveDownloadsDto>>()
            .Handle(new GetLiveDownloadsQuery(), cancellationToken));

    public Task<int> CancelSheetDownloadsAsync(bool onlyFinished, CancellationToken cancellationToken = default) =>
        realData.RunAsync(sp => sp.GetRequiredService<ICommandHandler<TercerTiempo.Application.OfficialSheets.CancelSheetDownloadsCommand, int>>()
            .Handle(new TercerTiempo.Application.OfficialSheets.CancelSheetDownloadsCommand(onlyFinished), cancellationToken));

    public async Task<IReadOnlyList<TercerTiempo.Application.Seasons.SeasonYearDto>> SetCurrentSeasonAsync(Guid seasonYearId, CancellationToken cancellationToken = default)
    {
        var seasons = await realData.RunAsync(sp => sp
            .GetRequiredService<ICommandHandler<TercerTiempo.Application.Seasons.SetCurrentSeasonCommand, IReadOnlyList<TercerTiempo.Application.Seasons.SeasonYearDto>>>()
            .Handle(new TercerTiempo.Application.Seasons.SetCurrentSeasonCommand(seasonYearId), cancellationToken));
        realData.RequestCopy();
        return seasons;
    }

    public Task<TercerTiempo.Application.Privacy.AnalyticsReportDto> GetAnalyticsAsync(TercerTiempo.Application.Privacy.GetAnalyticsReportQuery query, CancellationToken cancellationToken = default) =>
        analytics.Handle(query, cancellationToken);

    public Task<TercerTiempo.Application.Privacy.AdminCookiePolicyDto> GetCookiePolicyAsync(CancellationToken cancellationToken = default) =>
        cookiePolicy.Handle(new TercerTiempo.Application.Privacy.GetAdminCookiePolicyQuery(), cancellationToken);

    public Task<TercerTiempo.Application.Privacy.AdminCookiePolicyDto> UpsertCookieAsync(TercerTiempo.Application.Privacy.UpsertCookieCommand command, CancellationToken cancellationToken = default) =>
        upsertCookie.Handle(command, cancellationToken);

    public Task<TercerTiempo.Application.Privacy.AdminCookiePolicyDto> DeleteCookieAsync(Guid id, CancellationToken cancellationToken = default) =>
        deleteCookie.Handle(new TercerTiempo.Application.Privacy.DeleteCookieCommand(id), cancellationToken);

    public Task<TercerTiempo.Application.Privacy.AdminCookiePolicyDto> PublishCookiePolicyAsync(TercerTiempo.Application.Privacy.PublishCookiePolicyCommand command, CancellationToken cancellationToken = default) =>
        publishCookies.Handle(command, cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminInjuryTypeDto>> GetInjuryTypesAsync(CancellationToken cancellationToken = default) =>
        injuryTypes.Handle(new TercerTiempo.Application.Squads.Injuries.ListAdminInjuryTypesQuery(), cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminInjuryTypeDto>> SaveInjuryTypeAsync(TercerTiempo.Application.Squads.Injuries.SaveInjuryTypeCommand command, CancellationToken cancellationToken = default) =>
        saveInjuryType.Handle(command, cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminInjuryTypeDto>> DeleteInjuryTypeAsync(string code, CancellationToken cancellationToken = default) =>
        deleteInjuryType.Handle(new TercerTiempo.Application.Squads.Injuries.DeleteInjuryTypeCommand(code), cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminBodySiteDto>> GetBodySitesAsync(CancellationToken cancellationToken = default) =>
        bodySites.Handle(new TercerTiempo.Application.Squads.Injuries.ListAdminBodySitesQuery(), cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminBodySiteDto>> SaveBodySiteAsync(TercerTiempo.Application.Squads.Injuries.SaveBodySiteCommand command, CancellationToken cancellationToken = default) =>
        saveBodySite.Handle(command, cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Squads.Injuries.AdminBodySiteDto>> DeleteBodySiteAsync(string code, CancellationToken cancellationToken = default) =>
        deleteBodySite.Handle(new TercerTiempo.Application.Squads.Injuries.DeleteBodySiteCommand(code), cancellationToken);

    public Task<IReadOnlyList<TercerTiempo.Application.Licensing.PaymentEventDto>> GetPaymentEventsAsync(Guid? licenseId, int? max, CancellationToken cancellationToken = default) =>
        paymentEvents.Handle(new TercerTiempo.Application.Licensing.ListPaymentEventsQuery(licenseId, max ?? 100), cancellationToken);

    public Task<bool> RetryPaymentEventAsync(Guid id, CancellationToken cancellationToken = default) =>
        retryPaymentEvent.Handle(new TercerTiempo.Application.Licensing.RetryPaymentEventCommand(id), cancellationToken);

    public Task<string> SimulateLicenseEventAsync(Guid licenseId, TercerTiempo.Application.Licensing.SimulatedLicenseEvent kind, CancellationToken cancellationToken = default) =>
        simulateLicenseEvent.Handle(new TercerTiempo.Application.Licensing.SimulateLicenseEventCommand(licenseId, kind), cancellationToken);

    public Task<LicensesOverviewDto> GetLicensesAsync(CancellationToken cancellationToken = default) =>
        licenses.Handle(new GetLicensesOverviewQuery(), cancellationToken);

    public Task<LicensingSettingsDto> GetLicensingSettingsAsync(CancellationToken cancellationToken = default) =>
        licensingSettings.Handle(new GetLicensingSettingsQuery(), cancellationToken);

    public Task<LicensingSettingsDto> UpdateLicensingSettingsAsync(UpdateLicensingSettingsCommand command, CancellationToken cancellationToken = default) =>
        updateLicensingSettings.Handle(command, cancellationToken);

    public Task<IReadOnlyList<ClubInvitationDto>> GetClubInvitationsAsync(CancellationToken cancellationToken = default) =>
        listInvitations.Handle(new ListClubInvitationsQuery(), cancellationToken);

    public Task<IReadOnlyList<InvitationClubDto>> SearchInvitationClubsAsync(string? query, CancellationToken cancellationToken = default) =>
        searchInvitationClubs.Handle(new SearchInvitationClubsQuery(query), cancellationToken);

    public Task<ClubInvitationDto> CreateClubInvitationAsync(CreateClubInvitationCommand command, CancellationToken cancellationToken = default) =>
        createInvitation.Handle(command, cancellationToken);

    public Task<ClubInvitationDto> RevokeClubInvitationAsync(Guid id, CancellationToken cancellationToken = default) =>
        revokeInvitation.Handle(new RevokeClubInvitationCommand(id), cancellationToken);

    public async Task<TeamCrestImportDto> ImportTeamCrestAsync(Guid teamId, string url, CancellationToken cancellationToken = default)
    {
        var crest = await realData.RunAsync(sp => sp.GetRequiredService<ICommandHandler<ImportTeamCrestCommand, TeamCrestImportDto>>()
            .Handle(new ImportTeamCrestCommand(teamId, url), cancellationToken));
        realData.RequestCopy();
        return crest;
    }
}

public sealed record ImportCrestRequest(string Url);
