using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Infrastructure.Licensing;

// Eventos con el formato de Stripe: los lee el proceso de la cola, vengan de Stripe o de la pasarela simulada, que los
// genera iguales. Así el día que se conecte Stripe el camino de los avisos es exactamente el mismo que se ha probado.
internal static class StripeEvents
{
    public static ReceivedWebhook Read(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return new ReceivedWebhook(Text(json.RootElement, "id") ?? string.Empty, Text(json.RootElement, "type") ?? string.Empty);
    }

    public static LicenseWebhookEvent? Parse(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var type = Text(json.RootElement, "type");
        var data = json.RootElement.GetProperty("data").GetProperty("object");

        switch (type)
        {
            case "checkout.session.completed" when Text(data, "mode") == "subscription":
                return new CheckoutCompletedEvent(Text(data, "id")!);

            case "invoice.paid":
            {
                var (subscriptionRef, licenseId) = Subscription(data);
                if (subscriptionRef is null) return null;
                var line = data.GetProperty("lines").GetProperty("data")[0];
                var periodEnd = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(line.GetProperty("period").GetProperty("end").GetInt64()).UtcDateTime);
                return new InvoicePaidEvent(
                    Text(data, "id")!, subscriptionRef, licenseId, Text(data, "billing_reason") ?? string.Empty,
                    data.GetProperty("amount_paid").GetInt32(), Currency(data), periodEnd);
            }

            case "invoice.payment_failed":
            {
                var (subscriptionRef, licenseId) = Subscription(data);
                if (subscriptionRef is null) return null;
                DateTimeOffset? next = data.TryGetProperty("next_payment_attempt", out var attempt) && attempt.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(attempt.GetInt64())
                    : null;
                var reason = data.TryGetProperty("last_finalization_error", out var error) && error.ValueKind == JsonValueKind.Object
                    ? Text(error, "message")
                    : null;
                return new InvoicePaymentFailedEvent(
                    Text(data, "id")!, subscriptionRef, licenseId,
                    data.TryGetProperty("amount_due", out var due) ? due.GetInt32() : 0, Currency(data),
                    data.TryGetProperty("attempt_count", out var count) ? count.GetInt32() : 1,
                    next, reason ?? "La entidad ha rechazado el cobro");
            }

            case "customer.subscription.deleted":
                return new SubscriptionEndedEvent(Text(data, "id")!);

            default:
                return null;
        }
    }

    // Hasta la API 2025-03 la suscripción y su metadata colgaban de la factura; después, de «parent».
    private static (string? SubscriptionRef, Guid? LicenseId) Subscription(JsonElement invoice)
    {
        var details = invoice.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty("subscription_details", out var parentDetails) && parentDetails.ValueKind == JsonValueKind.Object
            ? parentDetails
            : invoice.TryGetProperty("subscription_details", out var legacyDetails) ? legacyDetails : default;
        var subscriptionRef = Text(invoice, "subscription") ?? (details.ValueKind == JsonValueKind.Object ? Text(details, "subscription") : null);
        Guid? licenseId = details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            && Guid.TryParse(Text(metadata, "license_id"), out var id) ? id : null;
        return (subscriptionRef, licenseId);
    }

    private static string Currency(JsonElement invoice) => (Text(invoice, "currency") ?? LicensingSettings.DefaultCurrency).ToUpperInvariant();

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // --- Eventos que genera la pasarela simulada, con los campos que lee Parse.

    public static string CheckoutCompleted(string eventId, string sessionId, string subscriptionRef, string customerRef, DateTimeOffset now) =>
        Envelope(eventId, "checkout.session.completed", now, new JsonObject
        {
            ["id"] = sessionId,
            ["object"] = "checkout.session",
            ["mode"] = "subscription",
            ["status"] = "complete",
            ["subscription"] = subscriptionRef,
            ["customer"] = customerRef,
        });

    public static string InvoicePaid(string eventId, string invoiceId, string subscriptionRef, Guid licenseId, string billingReason, int amountCents, string currency, DateOnly periodEnd, DateTimeOffset now) =>
        Envelope(eventId, "invoice.paid", now, Invoice(invoiceId, subscriptionRef, licenseId, billingReason, currency, periodEnd, new()
        {
            ["status"] = "paid",
            ["amount_paid"] = amountCents,
            ["amount_due"] = amountCents,
        }));

    public static string InvoicePaymentFailed(
        string eventId, string invoiceId, string subscriptionRef, Guid licenseId, int amountCents, string currency, DateOnly periodEnd,
        int attempt, DateTimeOffset? nextAttempt, string code, string message, DateTimeOffset now) =>
        Envelope(eventId, "invoice.payment_failed", now, Invoice(invoiceId, subscriptionRef, licenseId, "subscription_cycle", currency, periodEnd, new()
        {
            ["status"] = "open",
            ["amount_paid"] = 0,
            ["amount_due"] = amountCents,
            ["attempt_count"] = attempt,
            ["next_payment_attempt"] = nextAttempt?.ToUnixTimeSeconds(),
            ["last_finalization_error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }));

    public static string SubscriptionDeleted(string eventId, string subscriptionRef, Guid licenseId, DateTimeOffset now) =>
        Envelope(eventId, "customer.subscription.deleted", now, new JsonObject
        {
            ["id"] = subscriptionRef,
            ["object"] = "subscription",
            ["status"] = "canceled",
            ["metadata"] = new JsonObject { ["license_id"] = licenseId.ToString() },
        });

    private static JsonObject Invoice(string invoiceId, string subscriptionRef, Guid licenseId, string billingReason, string currency, DateOnly periodEnd, JsonObject extra)
    {
        var end = new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var invoice = new JsonObject
        {
            ["id"] = invoiceId,
            ["object"] = "invoice",
            ["billing_reason"] = billingReason,
            ["currency"] = currency.ToLowerInvariant(),
            ["parent"] = new JsonObject
            {
                ["type"] = "subscription_details",
                ["subscription_details"] = new JsonObject
                {
                    ["subscription"] = subscriptionRef,
                    ["metadata"] = new JsonObject { ["license_id"] = licenseId.ToString() },
                },
            },
            ["lines"] = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["period"] = new JsonObject { ["end"] = end } }) },
        };
        foreach (var (key, value) in extra.ToList())
        {
            extra.Remove(key);
            invoice[key] = value;
        }
        return invoice;
    }

    private static string Envelope(string eventId, string type, DateTimeOffset now, JsonObject data) => new JsonObject
    {
        ["id"] = eventId,
        ["object"] = "event",
        ["type"] = type,
        ["created"] = now.ToUnixTimeSeconds(),
        ["livemode"] = false,
        ["data"] = new JsonObject { ["object"] = data },
    }.ToJsonString();

    public static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}".ToString(CultureInfo.InvariantCulture);
}
