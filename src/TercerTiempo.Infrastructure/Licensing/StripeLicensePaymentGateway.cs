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
// - Alta: Checkout Session en modo suscripción con dos líneas, la licencia de club (cantidad 1) y las licencias de técnico
//   (cantidad = técnicos, solo si hay), con los precios configurados (price_...) o, si faltan, los importes de la
//   configuración; si al club le queda prueba o periodo pagado, trial_end para no cobrar hasta entonces.
// - Técnicos a mitad de periodo: se cambia la cantidad de su línea; al añadirlos con un periodo pagado, Stripe cobra ya
//   la parte proporcional (always_invoice) y avisa con una factura «subscription_update».
// - Renovaciones, cancelaciones y cobros fallidos: los gestiona Stripe y avisa por webhook (invoice.paid,
//   invoice.payment_failed, customer.subscription.deleted); la licencia guarda el id de la suscripción y la de la
//   licencia va en su metadata. Los avisos entran en la cola de eventos y se aplican desde allí (StripeEvents).
internal sealed class StripeLicensePaymentGateway(HttpClient http, IOptions<LicensingOptions> options, ILicensingCatalog catalog, TimeProvider clock) : ILicensePaymentGateway
{
    // Stripe exige que trial_end esté al menos 48 h en el futuro.
    private static readonly TimeSpan MinimumTrial = TimeSpan.FromHours(49);

    // Marca de cada línea en los productos que crea la app (product metadata «item»).
    private const string ClubItem = "club";
    private const string StaffSeatItem = "staff_seat";

    public string Provider => "stripe";

    public bool RenewsByWebhook => true;

    private StripeOptions Stripe => options.Value.Stripe;

    public async Task<CheckoutSession> BeginCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var settings = await catalog.GetAsync(cancellationToken);
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
            new("metadata[license_id]", request.LicenseId.ToString()),
            new("metadata[club_id]", request.ClubId.ToString()),
            new("subscription_data[metadata][license_id]", request.LicenseId.ToString()),
            new("subscription_data[metadata][club_id]", request.ClubId.ToString()),
            new("subscription_data[description]", $"Licencia de {Subscription(request.StaffSeats)} · {request.ClubName}"),
        };

        AddLineItem(form, 0, settings.For(LicenseItem.Club), request.Interval, request.ClubPriceCents, 1, request.Currency);
        if (request.StaffSeats > 0)
            AddLineItem(form, 1, settings.For(LicenseItem.StaffSeat), request.Interval, request.StaffSeatPriceCents, request.StaffSeats, request.Currency);

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

    // Cambia la facturación y los técnicos sin prorratear: el periodo en curso se queda como está y la próxima factura
    // ya sale con lo nuevo.
    public async Task ChangeRenewalAsync(string subscriptionRef, BillingInterval interval, int staffSeats, CancellationToken cancellationToken)
    {
        var settings = await catalog.GetAsync(cancellationToken);
        var lines = await LinesAsync(subscriptionRef, settings, cancellationToken);
        var form = new List<KeyValuePair<string, string>> { new("proration_behavior", "none") };

        form.Add(new("items[0][id]", lines.Club.Id));
        AddItemPrice(form, 0, settings.For(LicenseItem.Club), interval, lines.Club.ProductId, settings.Currency);
        await AddStaffSeatsAsync(form, 1, lines.StaffSeat, staffSeats, settings, interval, cancellationToken);

        using var _ = await SendAsync(HttpMethod.Post, $"v1/subscriptions/{Uri.EscapeDataString(subscriptionRef)}", form, cancellationToken);
    }

    public async Task ChangeStaffSeatsAsync(StaffSeatsChange change, CancellationToken cancellationToken)
    {
        var settings = await catalog.GetAsync(cancellationToken);
        var lines = await LinesAsync(change.SubscriptionRef, settings, cancellationToken);
        var form = new List<KeyValuePair<string, string>>
        {
            new("proration_behavior", change.ChargeNowCents > 0 ? "always_invoice" : "none"),
        };
        await AddStaffSeatsAsync(form, 0, lines.StaffSeat, change.StaffSeats, settings, lines.Interval, cancellationToken);

        using var _ = await SendAsync(HttpMethod.Post, $"v1/subscriptions/{Uri.EscapeDataString(change.SubscriptionRef)}", form, cancellationToken);
    }

    public ReceivedWebhook ReadWebhook(string payload, string? signature)
    {
        Verify(payload, signature);
        return StripeEvents.Read(payload);
    }

    // Firma comprobada y evento traducido (lo que hace la cola al aplicarlo); null si no es un evento que importe.
    public LicenseWebhookEvent? ParseWebhook(string payload, string? signature)
    {
        Verify(payload, signature);
        return StripeEvents.Parse(payload);
    }

    private void Verify(string payload, string? signature)
    {
        if (!StripeSignature.IsValid(payload, signature, Stripe.WebhookSecret, clock.GetUtcNow(), TimeSpan.FromSeconds(Stripe.WebhookToleranceSeconds)))
            throw new DomainException(ErrorCode.LicenseWebhookInvalid);
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

    private sealed record Line(string Id, string ProductId, int Quantity);

    private sealed record Lines(Line Club, Line? StaffSeat, BillingInterval Interval);

    // Líneas de la suscripción: la de club y, si la hay, la de técnicos. Las suscripciones de antes de las licencias de
    // técnico tienen una sola línea, que es la de club.
    private async Task<Lines> LinesAsync(string subscriptionRef, LicensingSettings settings, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Get, $"v1/subscriptions/{Uri.EscapeDataString(subscriptionRef)}?expand[]=items.data.price.product", null, cancellationToken);
        Line? club = null;
        Line? seats = null;
        var interval = BillingInterval.Monthly;
        foreach (var item in json.RootElement.GetProperty("items").GetProperty("data").EnumerateArray())
        {
            var price = item.GetProperty("price");
            var product = price.GetProperty("product");
            var line = new Line(
                item.GetProperty("id").GetString()!,
                product.ValueKind == JsonValueKind.Object ? Text(product, "id")! : product.GetString()!,
                item.TryGetProperty("quantity", out var quantity) && quantity.ValueKind == JsonValueKind.Number ? quantity.GetInt32() : 1);
            if (IsStaffSeat(price, product, settings))
            {
                seats = line;
            }
            else
            {
                club ??= line;
                if (price.TryGetProperty("recurring", out var recurring) && Text(recurring, "interval") == "year") interval = BillingInterval.Yearly;
            }
        }
        return new Lines(club ?? throw new InvalidOperationException($"La suscripción {subscriptionRef} no tiene la línea de la licencia de club."), seats, interval);
    }

    private static bool IsStaffSeat(JsonElement price, JsonElement product, LicensingSettings settings)
    {
        var seat = settings.For(LicenseItem.StaffSeat);
        var priceId = Text(price, "id");
        if (priceId is not null && (priceId == seat.StripeMonthlyPriceId || priceId == seat.StripeYearlyPriceId)) return true;
        return product.ValueKind == JsonValueKind.Object
            && product.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            && Text(metadata, "item") == StaffSeatItem;
    }

    // Línea de técnicos en una actualización de la suscripción: cambia su cantidad, la quita si se queda en 0 o la crea.
    private async Task AddStaffSeatsAsync(
        List<KeyValuePair<string, string>> form, int index, Line? current, int staffSeats, LicensingSettings settings, BillingInterval interval,
        CancellationToken cancellationToken)
    {
        var key = $"items[{index}]";
        if (current is not null)
        {
            form.Add(new($"{key}[id]", current.Id));
            if (staffSeats == 0)
            {
                form.Add(new($"{key}[deleted]", "true"));
                return;
            }
            form.Add(new($"{key}[quantity]", staffSeats.ToString(CultureInfo.InvariantCulture)));
            AddItemPrice(form, index, settings.For(LicenseItem.StaffSeat), interval, current.ProductId, settings.Currency);
            return;
        }

        if (staffSeats == 0) return;
        var price = settings.For(LicenseItem.StaffSeat);
        var productId = price.StripePriceId(interval) is { Length: > 0 } ? string.Empty : await CreateProductAsync(LicenseItem.StaffSeat, cancellationToken);
        form.Add(new($"{key}[quantity]", staffSeats.ToString(CultureInfo.InvariantCulture)));
        AddItemPrice(form, index, price, interval, productId, settings.Currency);
    }

    private async Task<string> CreateProductAsync(LicenseItem item, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(HttpMethod.Post, "v1/products",
            [new("name", ProductName(item)), new("metadata[item]", ItemKey(item))], cancellationToken);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    private static void AddLineItem(
        List<KeyValuePair<string, string>> form, int index, LicensePriceSettings price, BillingInterval interval, int unitAmountCents, int quantity, string currency)
    {
        var key = $"line_items[{index}]";
        form.Add(new($"{key}[quantity]", quantity.ToString(CultureInfo.InvariantCulture)));
        if (price.StripePriceId(interval) is { Length: > 0 } priceId)
        {
            form.Add(new($"{key}[price]", priceId));
            return;
        }
        form.Add(new($"{key}[price_data][currency]", currency.ToLowerInvariant()));
        form.Add(new($"{key}[price_data][unit_amount]", unitAmountCents.ToString(CultureInfo.InvariantCulture)));
        form.Add(new($"{key}[price_data][recurring][interval]", Recurring(interval)));
        form.Add(new($"{key}[price_data][product_data][name]", ProductName(price.Item)));
        form.Add(new($"{key}[price_data][product_data][metadata][item]", ItemKey(price.Item)));
    }

    private static void AddItemPrice(List<KeyValuePair<string, string>> form, int index, LicensePriceSettings price, BillingInterval interval, string productId, string currency)
    {
        var key = $"items[{index}]";
        if (price.StripePriceId(interval) is { Length: > 0 } priceId)
        {
            form.Add(new($"{key}[price]", priceId));
            return;
        }
        form.Add(new($"{key}[price_data][product]", productId));
        form.Add(new($"{key}[price_data][currency]", currency.ToLowerInvariant()));
        form.Add(new($"{key}[price_data][unit_amount]", price.PriceCents(interval).ToString(CultureInfo.InvariantCulture)));
        form.Add(new($"{key}[price_data][recurring][interval]", Recurring(interval)));
    }

    private static string Recurring(BillingInterval interval) => interval == BillingInterval.Yearly ? "year" : "month";

    private static string ItemKey(LicenseItem item) => item == LicenseItem.StaffSeat ? StaffSeatItem : ClubItem;

    private static string ProductName(LicenseItem item) =>
        item == LicenseItem.StaffSeat ? "Tercer Tiempo · Licencia de técnico" : "Tercer Tiempo · Licencia de club";

    private static string Subscription(int staffSeats) =>
        staffSeats switch { 0 => "club", 1 => "club + 1 técnico", _ => $"club + {staffSeats} técnicos" };
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
