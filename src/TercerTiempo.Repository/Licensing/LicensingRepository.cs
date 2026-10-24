using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Repository.Licensing;

public interface ILicensingRepository
{
    Task<LicensingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<LicensingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamLicenseDto>> ListClubLicensesAsync(CancellationToken cancellationToken = default);
    Task<LicenseCheckoutDto> BeginCheckoutAsync(Guid teamId, LicensePlanRequest request, CancellationToken cancellationToken = default);
    Task<LicenseCheckoutDto> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<TeamLicenseDto> ConfirmPaymentAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<TeamLicenseDto> ChangeRenewalAsync(Guid teamId, LicensePlanRequest request, CancellationToken cancellationToken = default);
    Task<TeamLicenseDto> CancelRenewalAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<TeamLicenseDto> ResumeRenewalAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<bool> HandleWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default);
}

public sealed record LicensePlanRequest(
    [property: Description("Basic (1 técnico) o Premium (cuerpo técnico ampliado).")]
    LicensePlan Plan,
    [property: Description("Monthly (cobro cada mes) o Yearly (un cobro al año).")]
    BillingInterval Interval);

internal sealed class LicensingRepository(
    IQueryHandler<GetLicensingCatalogQuery, LicensingCatalogDto> getCatalog,
    IQueryHandler<GetLicensingStatusQuery, LicensingStatusDto> getStatus,
    IQueryHandler<ListClubLicensesQuery, IReadOnlyList<TeamLicenseDto>> listClubLicenses,
    ICommandHandler<BeginLicenseCheckoutCommand, LicenseCheckoutDto> beginCheckout,
    IQueryHandler<GetLicenseCheckoutQuery, LicenseCheckoutDto> getCheckout,
    ICommandHandler<ConfirmLicensePaymentCommand, TeamLicenseDto> confirmPayment,
    ICommandHandler<ChangeLicenseRenewalCommand, TeamLicenseDto> changeRenewal,
    ICommandHandler<CancelLicenseRenewalCommand, TeamLicenseDto> cancelRenewal,
    ICommandHandler<ResumeLicenseRenewalCommand, TeamLicenseDto> resumeRenewal,
    ICommandHandler<HandleLicenseWebhookCommand, bool> handleWebhook) : ILicensingRepository
{
    public Task<LicensingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        getCatalog.Handle(new GetLicensingCatalogQuery(), cancellationToken);

    public Task<LicensingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        getStatus.Handle(new GetLicensingStatusQuery(), cancellationToken);

    public Task<IReadOnlyList<TeamLicenseDto>> ListClubLicensesAsync(CancellationToken cancellationToken = default) =>
        listClubLicenses.Handle(new ListClubLicensesQuery(), cancellationToken);

    public Task<LicenseCheckoutDto> BeginCheckoutAsync(Guid teamId, LicensePlanRequest request, CancellationToken cancellationToken = default) =>
        beginCheckout.Handle(new BeginLicenseCheckoutCommand(teamId, request.Plan, request.Interval), cancellationToken);

    public Task<LicenseCheckoutDto> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default) =>
        getCheckout.Handle(new GetLicenseCheckoutQuery(sessionId), cancellationToken);

    public Task<TeamLicenseDto> ConfirmPaymentAsync(string sessionId, CancellationToken cancellationToken = default) =>
        confirmPayment.Handle(new ConfirmLicensePaymentCommand(sessionId), cancellationToken);

    public Task<TeamLicenseDto> ChangeRenewalAsync(Guid teamId, LicensePlanRequest request, CancellationToken cancellationToken = default) =>
        changeRenewal.Handle(new ChangeLicenseRenewalCommand(teamId, request.Plan, request.Interval), cancellationToken);

    public Task<TeamLicenseDto> CancelRenewalAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        cancelRenewal.Handle(new CancelLicenseRenewalCommand(teamId), cancellationToken);

    public Task<TeamLicenseDto> ResumeRenewalAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        resumeRenewal.Handle(new ResumeLicenseRenewalCommand(teamId), cancellationToken);

    public Task<bool> HandleWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default) =>
        handleWebhook.Handle(new HandleLicenseWebhookCommand(payload, signature), cancellationToken);
}
