using System.Net;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Licensing;

namespace TercerTiempo.Application.Tests;

public class StripeLicenseGatewayTests
{
    private const string Secret = "whsec_test";
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StripeLicensePaymentGateway Gateway() => new(
        new HttpClient(),
        Options.Create(new LicensingOptions { Stripe = new StripeOptions { WebhookSecret = Secret } }),
        new FixedCatalog(),
        new FixedClock(Now));

    private sealed class FixedCatalog : ILicensingCatalog
    {
        public Task<TercerTiempo.Domain.Licensing.LicensingSettings> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(TercerTiempo.Domain.Licensing.LicensingSettings.CreateDefault(Now));

        public void Invalidate() { }
    }

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

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Checkout_charges_the_club_license_and_the_coaches_as_separate_lines(int staffSeats)
    {
        var stripe = new StripeStub(_ => """{"id":"cs_1","url":"https://checkout.stripe.test/cs_1"}""");

        await Gateway(stripe).BeginCheckoutAsync(
            new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), "CD Vallobín", BillingInterval.Monthly, staffSeats, 490, 250, "EUR", null, null), default);

        var form = Assert.Single(stripe.Requests).Form;
        Assert.Equal(("1", "490", "club"), (form["line_items[0][quantity]"], form["line_items[0][price_data][unit_amount]"], form["line_items[0][price_data][product_data][metadata][item]"]));
        if (staffSeats == 0)
        {
            Assert.DoesNotContain("line_items[1][quantity]", form.Keys);
        }
        else
        {
            Assert.Equal(("3", "250", "staff_seat"), (form["line_items[1][quantity]"], form["line_items[1][price_data][unit_amount]"], form["line_items[1][price_data][product_data][metadata][item]"]));
        }
    }

    [Fact]
    public async Task Adding_coaches_mid_period_changes_their_quantity_and_charges_the_proration_at_once()
    {
        var stripe = new StripeStub(request => request.Method == HttpMethod.Get ? Subscription(withSeats: true) : """{"id":"sub_1"}""");

        await Gateway(stripe).ChangeStaffSeatsAsync(new StaffSeatsChange(Guid.NewGuid(), "sub_1", 3, 125, new DateOnly(2026, 11, 20)), default);

        var update = stripe.Requests.Last().Form;
        Assert.Equal(("always_invoice", "si_seat", "3"), (update["proration_behavior"], update["items[0][id]"], update["items[0][quantity]"]));
        Assert.Equal(("prod_seat", "250"), (update["items[0][price_data][product]"], update["items[0][price_data][unit_amount]"]));
    }

    [Fact]
    public async Task A_subscription_from_the_old_plans_gets_a_coach_line_at_the_next_renewal()
    {
        var stripe = new StripeStub(request =>
            request.Method == HttpMethod.Get ? Subscription(withSeats: false)
            : request.RequestUri!.AbsolutePath.EndsWith("/products") ? """{"id":"prod_new"}"""
            : """{"id":"sub_1"}""");

        await Gateway(stripe).ChangeRenewalAsync("sub_1", BillingInterval.Yearly, 2, default);

        Assert.Equal(("staff_seat", "Tercer Tiempo · Licencia de técnico"), (stripe.Requests[1].Form["metadata[item]"], stripe.Requests[1].Form["name"]));
        var update = stripe.Requests.Last().Form;
        Assert.Equal("none", update["proration_behavior"]);
        Assert.Equal(("si_club", "prod_club", "4900", "year"),
            (update["items[0][id]"], update["items[0][price_data][product]"], update["items[0][price_data][unit_amount]"], update["items[0][price_data][recurring][interval]"]));
        Assert.Equal(("2", "prod_new", "2500"), (update["items[1][quantity]"], update["items[1][price_data][product]"], update["items[1][price_data][unit_amount]"]));
        Assert.DoesNotContain("items[1][id]", update.Keys);
    }

    private static StripeLicensePaymentGateway Gateway(StripeStub stripe) => new(
        new HttpClient(stripe) { BaseAddress = new Uri("https://api.stripe.test/") },
        Options.Create(new LicensingOptions { Stripe = new StripeOptions { SecretKey = "sk_test", WebhookSecret = Secret } }),
        new FixedCatalog(),
        new FixedClock(Now));

    // Suscripción con la línea de club y, si se pide, la de técnicos (con la marca «item» en sus productos).
    private static string Subscription(bool withSeats)
    {
        const string club = """{"id":"si_club","quantity":1,"price":{"id":"price_c","recurring":{"interval":"month"},"product":{"id":"prod_club","metadata":{"item":"club"}}}}""";
        const string seats = """{"id":"si_seat","quantity":1,"price":{"id":"price_s","recurring":{"interval":"month"},"product":{"id":"prod_seat","metadata":{"item":"staff_seat"}}}}""";
        return """{"id":"sub_1","items":{"data":[""" + club + (withSeats ? "," + seats : "") + "]}}";
    }

    private sealed class StripeStub(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, Dictionary<string, string> Form)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, form));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request)) };
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
