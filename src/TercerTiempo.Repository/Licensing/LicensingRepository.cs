using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;

namespace TercerTiempo.Repository.Licensing;

public interface ILicensingRepository
{
    Task<LicensingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamLicenseDto>> ListClubLicensesAsync(CancellationToken cancellationToken = default);
    Task<LicenseCheckoutDto> BeginCheckoutAsync(Guid teamId, LicenseCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<LicenseCheckoutDto> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<TeamLicenseDto> ConfirmPaymentAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed record LicenseCheckoutRequest(
    [property: Description("Meses que se pagan (1-12), a 10 € al mes por equipo.")]
    int Months);

internal sealed class LicensingRepository(
    IQueryHandler<GetLicensingStatusQuery, LicensingStatusDto> getStatus,
    IQueryHandler<ListClubLicensesQuery, IReadOnlyList<TeamLicenseDto>> listClubLicenses,
    ICommandHandler<BeginLicenseCheckoutCommand, LicenseCheckoutDto> beginCheckout,
    IQueryHandler<GetLicenseCheckoutQuery, LicenseCheckoutDto> getCheckout,
    ICommandHandler<ConfirmLicensePaymentCommand, TeamLicenseDto> confirmPayment) : ILicensingRepository
{
    public Task<LicensingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        getStatus.Handle(new GetLicensingStatusQuery(), cancellationToken);

    public Task<IReadOnlyList<TeamLicenseDto>> ListClubLicensesAsync(CancellationToken cancellationToken = default) =>
        listClubLicenses.Handle(new ListClubLicensesQuery(), cancellationToken);

    public Task<LicenseCheckoutDto> BeginCheckoutAsync(Guid teamId, LicenseCheckoutRequest request, CancellationToken cancellationToken = default) =>
        beginCheckout.Handle(new BeginLicenseCheckoutCommand(teamId, request.Months), cancellationToken);

    public Task<LicenseCheckoutDto> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken = default) =>
        getCheckout.Handle(new GetLicenseCheckoutQuery(sessionId), cancellationToken);

    public Task<TeamLicenseDto> ConfirmPaymentAsync(string sessionId, CancellationToken cancellationToken = default) =>
        confirmPayment.Handle(new ConfirmLicensePaymentCommand(sessionId), cancellationToken);
}
