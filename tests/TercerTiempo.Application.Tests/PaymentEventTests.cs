using System.Reflection;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Licensing;

namespace TercerTiempo.Application.Tests;

// Los eventos que genera la pasarela simulada tienen el formato de Stripe y siguen el mismo camino: se leen con el
// mismo código y se aplican a la licencia igual.
public class PaymentEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public void Simulated_events_read_like_the_ones_from_stripe()
    {
        var licenseId = Guid.NewGuid();

        var completed = Assert.IsType<CheckoutCompletedEvent>(StripeEvents.Parse(StripeEvents.CheckoutCompleted("evt_1", "cs_mock_1", "sub_1", "cus_1", Now)));
        Assert.Equal("cs_mock_1", completed.SessionId);

        var paid = Assert.IsType<InvoicePaidEvent>(StripeEvents.Parse(
            StripeEvents.InvoicePaid("evt_2", "in_1", "sub_1", licenseId, "subscription_cycle", 990, "EUR", Today.AddMonths(1), Now)));
        Assert.Equal(("in_1", "sub_1", licenseId, "subscription_cycle", 990, "EUR", Today.AddMonths(1)),
            (paid.InvoiceId, paid.SubscriptionRef, paid.LicenseId, paid.BillingReason, paid.AmountCents, paid.Currency, paid.PeriodEnd));

        var failed = Assert.IsType<InvoicePaymentFailedEvent>(StripeEvents.Parse(
            StripeEvents.InvoicePaymentFailed("evt_3", "in_1", "sub_1", licenseId, 990, "EUR", Today.AddMonths(1), 2, Now.AddDays(5), "card_declined", "Tarjeta rechazada", Now)));
        Assert.Equal((2, 990, "Tarjeta rechazada", Now.AddDays(5)), (failed.AttemptCount, failed.AmountCents, failed.Reason, failed.NextAttemptAt));

        Assert.Equal("sub_1", Assert.IsType<SubscriptionEndedEvent>(StripeEvents.Parse(StripeEvents.SubscriptionDeleted("evt_4", "sub_1", licenseId, Now))).SubscriptionRef);
        Assert.Equal(new ReceivedWebhook("evt_4", "customer.subscription.deleted"), StripeEvents.Read(StripeEvents.SubscriptionDeleted("evt_4", "sub_1", licenseId, Now)));
    }

    [Fact]
    public async Task A_paid_renewal_extends_the_license_once()
    {
        var store = new Store();
        var license = store.Subscribed();
        var periodEnd = license.ValidUntil.AddMonths(1);
        var paid = new InvoicePaidEvent("in_9", "sub_1", license.Id, "subscription_cycle", 990, "EUR", periodEnd);

        var first = await Handler(store).Handle(new ApplyLicenseEventCommand(paid), default);
        var again = await Handler(store).Handle(new ApplyLicenseEventCommand(paid), default);

        Assert.True(first.Applied);
        Assert.False(again.Applied);
        Assert.Equal(periodEnd, license.PaidUntil);
        Assert.Single(store.Payments, p => p.IsPaid && p.Kind == LicensePaymentKind.Renewal);
    }

    [Fact]
    public async Task Each_failed_attempt_is_recorded_once_and_the_last_one_ends_the_subscription()
    {
        var store = new Store();
        var license = store.Subscribed();

        foreach (var attempt in new[] { 1, 1, 2 })
            await Handler(store).Handle(new ApplyLicenseEventCommand(
                new InvoicePaymentFailedEvent("in_9", "sub_1", license.Id, 990, "EUR", attempt, Now.AddDays(3), "Tarjeta rechazada")), default);

        Assert.Equal(2, store.Payments.Count(p => p.Status == PaymentStatus.Failed && p.FailureReason == "Tarjeta rechazada"));
        Assert.True(license.AutoRenew);

        var ended = await Handler(store).Handle(new ApplyLicenseEventCommand(new SubscriptionEndedEvent("sub_1")), default);
        Assert.True(ended.Applied);
        Assert.False(license.AutoRenew);
    }

    [Fact]
    public async Task Coaches_added_mid_period_record_their_charge_without_renewing_or_applying_pending_changes()
    {
        var store = new Store();
        var license = store.Subscribed();
        var paidUntil = license.PaidUntil;
        license.ChangeRenewal(BillingInterval.Yearly, Today, Now);
        license.ChangeStaffSeats(3, Today, Now);

        var outcome = await Handler(store).Handle(new ApplyLicenseEventCommand(
            new InvoicePaidEvent("in_seats", "sub_1", license.Id, "subscription_update", 83, "EUR", paidUntil!.Value)), default);

        Assert.True(outcome.Applied);
        Assert.Equal(paidUntil, license.PaidUntil);
        Assert.Equal((BillingInterval.Monthly, BillingInterval.Yearly, 3), (license.Interval, license.NextInterval, license.StaffSeats));
        var payment = Assert.Single(store.Payments);
        Assert.Equal((LicensePaymentKind.StaffSeats, 83, 3, PaymentStatus.Paid), (payment.Kind, payment.AmountCents, payment.StaffSeats, payment.Status));
    }

    private static ApplyLicenseEventHandler Handler(Store store) => new(
        store.Proxy(),
        new MockGateway(),
        null!,
        Stub<TercerTiempo.Domain.Common.IUnitOfWork>((_, _) => Task.FromResult(1)),
        new FixedClock(Now));

    private sealed class Store
    {
        public List<ClubLicense> Licenses { get; } = [];
        public List<LicensePayment> Payments { get; } = [];

        public ClubLicense Subscribed()
        {
            var license = ClubLicense.Start(Guid.NewGuid(), Today.AddDays(-40), 30, Now.AddDays(-40));
            license.Subscribe(BillingInterval.Monthly, 1, "mock", "sub_1", "cus_1", Now.AddDays(-35));
            license.RecordPaidPeriod(Today.AddDays(5), Now.AddDays(-35));
            Licenses.Add(license);
            return license;
        }

        public ILicenseStore Proxy() => Stub<ILicenseStore>((method, args) => method switch
        {
            nameof(ILicenseStore.GetByIdAsync) => Task.FromResult(Licenses.FirstOrDefault(l => l.Id == (Guid)args![0]!)),
            nameof(ILicenseStore.GetBySubscriptionAsync) => Task.FromResult(Licenses.FirstOrDefault(l => l.SubscriptionRef == (string)args![0]!)),
            nameof(ILicenseStore.GetPaymentBySessionAsync) => Task.FromResult(Payments.FirstOrDefault(p => p.SessionId == (string)args![0]!)),
            nameof(ILicenseStore.AddPayment) => Add((LicensePayment)args![0]!),
            _ => throw new NotSupportedException(method),
        });

        private object? Add(LicensePayment payment)
        {
            Payments.Add(payment);
            return null;
        }
    }

    private sealed class MockGateway : ILicensePaymentGateway
    {
        public string Provider => "mock";
        public bool RenewsByWebhook => false;
        public Task<CheckoutSession> BeginCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CheckoutResult> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelRenewalAsync(string subscriptionRef, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeRenewalAsync(string subscriptionRef, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRenewalAsync(string subscriptionRef, BillingInterval interval, int staffSeats, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeStaffSeatsAsync(StaffSeatsChange change, CancellationToken cancellationToken) => Task.CompletedTask;
        public ReceivedWebhook ReadWebhook(string payload, string? signature) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
