using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Infrastructure.Licensing;

// Pasarela de Stripe (Licensing:Provider = "stripe"), por su API REST sin SDK:
// - Alta: Checkout Session en modo suscripción, con el precio del plan (price_... configurado o, si falta, el importe
//   de la configuración) y, si al equipo le queda prueba o periodo pagado, trial_end para no cobrar hasta entonces.
// - Renovaciones, cancelaciones y cobros fallidos: los gestiona Stripe y avisa por webhook (invoice.paid,
//   customer.subscription.deleted); la licencia guarda el id de la suscripción y la de la licencia va en su metadata.
internal sealed class StripeLicensePaymentGateway(HttpClient http, IOptions<LicensingOptions> options, TimeProvider clock) : ILicensePaymentGateway
{
    // Stripe exige que trial_end esté al menos 48 h en el futuro.
    private static readonly TimeSpan MinimumTrial = TimeSpan.FromHours(49);

    public string Provider => "stripe";

    public bool RenewsByWebhook => true;

    private StripeOptions Stripe => options.Value.Stripe;

    public async Task<CheckoutSession> BeginCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var plan = options.Value.For(request.Plan);
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("locale", "es"),
            new("success_url", Stripe.SuccessUrl),
            new("cancel_url", Stripe.CancelUrl),
            new("client_reference_id", request.LicenseId.ToString()),
            new("allow_promotion_codes", "true"),
            new("billing_address_collection", "required"),
            new("tax_id_collection[enabled]", "true"),
            new("line_items[0][quantity]", "1"),
            new("metadata[license_id]", request.LicenseId.ToString()),
            new("metadata[team_id]", request.TeamId.ToString()),
            new("metadata[club_id]", request.ClubId.ToString()),
            new("subscription_data[metadata][license_id]", request.LicenseId.ToString()),
            new("subscription_data[metadata][team_id]", request.TeamId.ToString()),
            new("subscription_data[description]", $"Licencia {LicenseName(request.Plan)} · {request.TeamName}"),
        };

        if (plan.StripePriceId(request.Interval) is { Length: > 0 } priceId)
        {
            form.Add(new("line_items[0][price]", priceId));
        }
        else
        {
            form.Add(new("line_items[0][price_data][currency]", request.Currency.ToLowerInvariant()));
            form.Add(new("line_items[0][price_data][unit_amount]", request.PeriodPriceCents.ToString(CultureInfo.InvariantCulture)));
            form.Add(new("line_items[0][price_data][recurring][interval]", request.Interval == BillingInterval.Yearly ? "year" : "month"));
            form.Add(new("line_items[0][price_data][product_data][name]", $"Tercer Tiempo · Licencia {LicenseName(request.Plan)}"));
        }

        if (request.DeferUntil is { } deferUntil)
        {
            var trialEnd = new DateTimeOffset(deferUntil.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var earliest = clock.GetUtcNow() + MinimumTrial;
            form.Add(new("subscription_data[trial_end]", (trialEnd > earliest ? trialEnd : earliest).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
            form.Add(new("payment_method_collection", "always"));
        }

        if (request.CustomerRef is { Length: > 0 } customer)
            form.Add(new("customer", customer));

        using var json = await SendAsync(HttpMethod.Post, "v1/checkout/sessions", form, cancellationToken);
        var root = json.RootElement;
        return new CheckoutSession(root.GetProperty("id").GetString()!, root.GetProperty("url").GetString()!);
    }

    public async Task<CheckoutResult> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get, $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}?expand[]=subscription", null, cancellationToken);
        var session = json.RootElement;
        if (Text(session, "status") != "complete") return new CheckoutResult(false, null, null, null);

        string? subscriptionRef = null;
        DateOnly? periodEnd = null;
        if (session.TryGetProperty("subscription", out var subscription) && subscription.ValueKind == JsonValueKind.Object)
        {
            subscriptionRef = Text(subscription, "id");
            if (Text(session, "payment_status") == "paid") periodEnd = PeriodEnd(subscription);
        }
        else if (subscription.ValueKind == JsonValueKind.String)
        {
            subscriptionRef = subscription.GetString();
        }

        return new CheckoutResult(true, subscriptionRef, Text(session, "customer"), periodEnd);
    }

    public async Task CancelRenewalAsync(string subscriptionRef, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"v1/subscriptions/{Uri.EscapeDataString(subscriptionRef)}",
            [new("cancel_at_period_end", "true")], cancellationToken);
    }

    public async Task ResumeRenewalAsync(string subscriptionRef, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"v1/subscriptions/{Uri.EscapeDataString(subscriptionRef)}",
            [new("cancel_at_period_end", "false")], cancellationToken);
    }

    // Cambia el precio de la suscripción sin prorratear: el periodo en curso se queda como está y la próxima factura
    // ya sale con el plan nuevo.
    public async Task ChangeRenewalAsync(string subscriptionRef, LicensePlan plan, BillingInterval interval, CancellationToken cancellationToken)
    {
        var path = $"v1/subscriptions/{Uri.EscapeDataString(subscriptionRef)}";
        using var current = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        var item = current.RootElement.GetProperty("items").GetProperty("data")[0];

        var form = new List<KeyValuePair<string, string>>
        {
            new("proration_behavior", "none"),
            new("items[0][id]", item.GetProperty("id").GetString()!),
        };
        if (options.Value.For(plan).StripePriceId(interval) is { Length: > 0 } priceId)
        {
            form.Add(new("items[0][price]", priceId));
        }
        else
        {
            var product = item.GetProperty("price").GetProperty("product");
            form.Add(new("items[0][price_data][product]", product.ValueKind == JsonValueKind.Object ? Text(product, "id")! : product.GetString()!));
            form.Add(new("items[0][price_data][currency]", options.Value.Currency.ToLowerInvariant()));
            form.Add(new("items[0][price_data][unit_amount]", options.Value.PriceCents(plan, interval).ToString(CultureInfo.InvariantCulture)));
            form.Add(new("items[0][price_data][recurring][interval]", interval == BillingInterval.Yearly ? "year" : "month"));
        }

        using var _ = await SendAsync(HttpMethod.Post, path, form, cancellationToken);
    }

    public LicenseWebhookEvent? ParseWebhook(string payload, string? signature)
    {
        if (!StripeSignature.IsValid(payload, signature, Stripe.WebhookSecret, clock.GetUtcNow(), TimeSpan.FromSeconds(Stripe.WebhookToleranceSeconds)))
            throw new DomainException(ErrorCode.LicenseWebhookInvalid);

        using var json = JsonDocument.Parse(payload);
        var type = Text(json.RootElement, "type");
        var data = json.RootElement.GetProperty("data").GetProperty("object");

        switch (type)
        {
            case "checkout.session.completed" when Text(data, "mode") == "subscription":
                return new CheckoutCompletedEvent(Text(data, "id")!);

            case "invoice.paid":
                // Hasta la API 2025-03 la suscripción y su metadata colgaban de la factura; después, de «parent».
                var details = data.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object
                    && parent.TryGetProperty("subscription_details", out var parentDetails) && parentDetails.ValueKind == JsonValueKind.Object
                    ? parentDetails
                    : data.TryGetProperty("subscription_details", out var legacyDetails) ? legacyDetails : default;
                var subscriptionRef = Text(data, "subscription") ?? (details.ValueKind == JsonValueKind.Object ? Text(details, "subscription") : null);
                if (subscriptionRef is null) return null;

                Guid? licenseId = details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                    && Guid.TryParse(Text(metadata, "license_id"), out var id) ? id : null;

                var line = data.GetProperty("lines").GetProperty("data")[0];
                var periodEnd = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(line.GetProperty("period").GetProperty("end").GetInt64()).UtcDateTime);

                return new InvoicePaidEvent(
                    Text(data, "id")!, subscriptionRef, licenseId, Text(data, "billing_reason") ?? string.Empty,
                    data.GetProperty("amount_paid").GetInt32(), (Text(data, "currency") ?? options.Value.Currency).ToUpperInvariant(), periodEnd);

            case "customer.subscription.deleted":
                return new SubscriptionEndedEvent(Text(data, "id")!);

            default:
                return null;
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>>? form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Stripe.SecretKey))
            throw new InvalidOperationException("Falta Licensing:Stripe:SecretKey para usar la pasarela de Stripe.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Stripe.SecretKey);
        if (!string.IsNullOrWhiteSpace(Stripe.ApiVersion)) request.Headers.Add("Stripe-Version", Stripe.ApiVersion);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Stripe respondió {(int)response.StatusCode} a {method} {path}: {body}", null, response.StatusCode);
        return JsonDocument.Parse(body);
    }

    // Fin del periodo en curso: en la suscripción (API antigua) o en su primera línea (API 2025-03 en adelante).
    private static DateOnly? PeriodEnd(JsonElement subscription)
    {
        long? end = subscription.TryGetProperty("current_period_end", out var top) && top.ValueKind == JsonValueKind.Number ? top.GetInt64() : null;
        if (end is null && subscription.TryGetProperty("items", out var items)
            && items.GetProperty("data") is { ValueKind: JsonValueKind.Array } data && data.GetArrayLength() > 0
            && data[0].TryGetProperty("current_period_end", out var itemEnd) && itemEnd.ValueKind == JsonValueKind.Number)
            end = itemEnd.GetInt64();
        return end is { } seconds ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime) : null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string LicenseName(LicensePlan plan) => plan == LicensePlan.Premium ? "Premium" : "Básica";
}

// Firma de los webhooks de Stripe: cabecera «t=<unix>,v1=<hmac>[,v1=...]» con HMAC-SHA256 de «t.payload» usando el
// secreto del endpoint, y el sello de tiempo dentro de la tolerancia para no aceptar avisos repetidos.
internal static class StripeSignature
{
    public static bool IsValid(string payload, string? header, string secret, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret)) return false;

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0] == "t" && long.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)) timestamp = t;
            else if (pair[0] == "v1") signatures.Add(pair[1]);
        }
        if (timestamp is null || signatures.Count == 0) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value)).Duration() > tolerance) return false;

        var expected = Compute(payload, timestamp.Value, secret);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(expected)));
    }

    public static string Compute(string payload, long timestamp, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.{payload}"));
        return Convert.ToHexStringLower(hash);
    }
}
