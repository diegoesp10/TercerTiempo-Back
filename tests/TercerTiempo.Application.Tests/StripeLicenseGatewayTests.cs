using Microsoft.Extensions.Options;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Infrastructure.Licensing;

namespace TercerTiempo.Application.Tests;

public class StripeLicenseGatewayTests
{
    private const string Secret = "whsec_test";
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StripeLicensePaymentGateway Gateway() => new(
        new HttpClient(),
        Options.Create(new LicensingOptions { Stripe = new StripeOptions { WebhookSecret = Secret } }),
        new FixedClock(Now));

    private static string Header(string payload, DateTimeOffset at) =>
        $"t={at.ToUnixTimeSeconds()},v1={StripeSignature.Compute(payload, at.ToUnixTimeSeconds(), Secret)}";

    [Fact]
    public void Webhooks_with_a_bad_or_stale_signature_are_rejected()
    {
        const string payload = """{"type":"customer.subscription.deleted","data":{"object":{"id":"sub_1"}}}""";

        Assert.Throws<DomainException>(() => Gateway().ParseWebhook(payload, "t=1,v1=abc"));
        Assert.Throws<DomainException>(() => Gateway().ParseWebhook(payload, Header(payload, Now.AddHours(-1))));
        Assert.Throws<DomainException>(() => Gateway().ParseWebhook(payload + " ", Header(payload, Now)));
    }

    [Fact]
    public void Subscription_end_is_read_from_a_signed_webhook()
    {
        const string payload = """{"type":"customer.subscription.deleted","data":{"object":{"id":"sub_1"}}}""";

        var parsed = Gateway().ParseWebhook(payload, Header(payload, Now));

        Assert.Equal(new SubscriptionEndedEvent("sub_1"), parsed);
    }

    [Fact]
    public void Paid_invoices_carry_the_license_and_period_in_both_api_shapes()
    {
        var licenseId = Guid.NewGuid();
        var periodEnd = new DateTimeOffset(2026, 12, 22, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var legacy = """
            {"type":"invoice.paid","data":{"object":{"id":"in_1","subscription":"sub_1","billing_reason":"subscription_cycle",
            "amount_paid":990,"currency":"eur","subscription_details":{"metadata":{"license_id":"LICENSE"}},
            "lines":{"data":[{"period":{"end":PERIOD_END}}]}}}}
            """.Replace("LICENSE", licenseId.ToString()).Replace("PERIOD_END", periodEnd.ToString());
        var current = """
            {"type":"invoice.paid","data":{"object":{"id":"in_2","billing_reason":"subscription_create","amount_paid":19900,"currency":"eur",
            "parent":{"subscription_details":{"subscription":"sub_2","metadata":{"license_id":"LICENSE"}}},
            "lines":{"data":[{"period":{"end":PERIOD_END}}]}}}}
            """.Replace("LICENSE", licenseId.ToString()).Replace("PERIOD_END", periodEnd.ToString());

        var first = Assert.IsType<InvoicePaidEvent>(Gateway().ParseWebhook(legacy, Header(legacy, Now)));
        var second = Assert.IsType<InvoicePaidEvent>(Gateway().ParseWebhook(current, Header(current, Now)));

        Assert.Equal(("sub_1", licenseId, 990, "EUR", new DateOnly(2026, 12, 22)), (first.SubscriptionRef, first.LicenseId, first.AmountCents, first.Currency, first.PeriodEnd));
        Assert.Equal(("sub_2", "subscription_create", 19900), (second.SubscriptionRef, second.BillingReason, second.AmountCents));
    }

    [Fact]
    public void Events_that_do_not_touch_licenses_are_ignored()
    {
        const string payload = """{"type":"payment_intent.created","data":{"object":{"id":"pi_1"}}}""";

        Assert.Null(Gateway().ParseWebhook(payload, Header(payload, Now)));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
