using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Repository.Licensing;

public interface ILicensingRepository
{
    Task<LicensingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<LicensingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ClubLicenseDto> GetClubLicenseAsync(CancellationToken cancellationToken = default);
    Task<LicenseCheckoutDto> BeginCheckoutAsync(LicenseCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<LicenseCheckoutDto> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ClubLicenseDto> ConfirmPaymentAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ClubLicenseDto> ChangeRenewalAsync(LicenseRenewalRequest request, CancellationToken cancellationToken = default);
    Task<ClubLicenseDto> ChangeStaffSeatsAsync(StaffSeatsRequest request, CancellationToken cancellationToken = default);
    Task<ClubLicenseDto> CancelRenewalAsync(CancellationToken cancellationToken = default);
    Task<ClubLicenseDto> ResumeRenewalAsync(CancellationToken cancellationToken = default);
    Task<bool> HandleWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default);
    Task<SimulatedPaymentDto> SimulatePaymentAsync(string sessionId, SimulatedPaymentRequest request, CancellationToken cancellationToken = default);
}

public sealed record SimulatedPaymentRequest(
    [property: Description("Resultado de la tarjeta de prueba: Succeeded, Declined, InsufficientFunds, ExpiredCard o AuthenticationFailed.")]
    SimulatedPaymentOutcome Outcome);

public sealed record LicenseCheckoutRequest(
    [property: Description("Monthly (cobro cada mes) o Yearly (un cobro al año).")]
    BillingInterval Interval,
    [property: Description("Licencias de técnico que se contratan ya con la de club (0 = solo el club; se pueden añadir después).")]
    int StaffSeats = 0);

public sealed record LicenseRenewalRequest(
    [property: Description("Monthly (cobro cada mes) o Yearly (un cobro al año).")]
    BillingInterval Interval);

public sealed record StaffSeatsRequest(
    [property: Description("Licencias de técnico que quiere tener el club (la bolsa para todos sus equipos).")]
    int StaffSeats);

internal sealed class LicensingRepository(
    IQueryHandler<GetLicensingCatalogQuery, LicensingCatalogDto> getCatalog,
    IQueryHandler<GetLicensingStatusQuery, LicensingStatusDto> getStatus,
    IQueryHandler<GetClubLicenseQuery, ClubLicenseDto> getClubLicense,
    ICommandHandler<BeginLicenseCheckoutCommand, LicenseCheckoutDto> beginCheckout,
    IQueryHandler<GetLicenseCheckoutQuery, LicenseCheckoutDto> getCheckout,
    ICommandHandler<ConfirmLicensePaymentCommand, ClubLicenseDto> confirmPayment,
    ICommandHandler<ChangeLicenseRenewalCommand, ClubLicenseDto> changeRenewal,
    ICommandHandler<ChangeStaffSeatsCommand, ClubLicenseDto> changeStaffSeats,
    ICommandHandler<CancelLicenseRenewalCommand, ClubLicenseDto> cancelRenewal,
    ICommandHandler<ResumeLicenseRenewalCommand, ClubLicenseDto> resumeRenewal,
    ICommandHandler<HandleLicenseWebhookCommand, bool> handleWebhook,
    ICommandHandler<SimulateCheckoutPaymentCommand, SimulatedPaymentDto> simulatePayment) : ILicensingRepository
{
    public Task<LicensingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        getCatalog.Handle(new GetLicensingCatalogQuery(), cancellationToken);

    public Task<LicensingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        getStatus.Handle(new GetLicensingStatusQuery(), cancellationToken);

    public Task<ClubLicenseDto> GetClubLicenseAsync(CancellationToken cancellationToken = default) =>
        getClubLicense.Handle(new GetClubLicenseQuery(), cancellationToken);

    public Task<LicenseCheckoutDto> BeginCheckoutAsync(LicenseCheckoutRequest request, CancellationToken cancellationToken = default) =>
        beginCheckout.Handle(new BeginLicenseCheckoutCommand(request.Interval, request.StaffSeats), cancellationToken);

    public Task<LicenseCheckoutDto> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default) =>
        getCheckout.Handle(new GetLicenseCheckoutQuery(sessionId), cancellationToken);

    public Task<ClubLicenseDto> ConfirmPaymentAsync(string sessionId, CancellationToken cancellationToken = default) =>
        confirmPayment.Handle(new ConfirmLicensePaymentCommand(sessionId), cancellationToken);

    public Task<ClubLicenseDto> ChangeRenewalAsync(LicenseRenewalRequest request, CancellationToken cancellationToken = default) =>
        changeRenewal.Handle(new ChangeLicenseRenewalCommand(request.Interval), cancellationToken);

    public Task<ClubLicenseDto> ChangeStaffSeatsAsync(StaffSeatsRequest request, CancellationToken cancellationToken = default) =>
        changeStaffSeats.Handle(new ChangeStaffSeatsCommand(request.StaffSeats), cancellationToken);

    public Task<ClubLicenseDto> CancelRenewalAsync(CancellationToken cancellationToken = default) =>
        cancelRenewal.Handle(new CancelLicenseRenewalCommand(), cancellationToken);

    public Task<ClubLicenseDto> ResumeRenewalAsync(CancellationToken cancellationToken = default) =>
        resumeRenewal.Handle(new ResumeLicenseRenewalCommand(), cancellationToken);

    public Task<bool> HandleWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default) =>
        handleWebhook.Handle(new HandleLicenseWebhookCommand(payload, signature), cancellationToken);

    public Task<SimulatedPaymentDto> SimulatePaymentAsync(string sessionId, SimulatedPaymentRequest request, CancellationToken cancellationToken = default) =>
        simulatePayment.Handle(new SimulateCheckoutPaymentCommand(sessionId, request.Outcome), cancellationToken);
}
