using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Licensing;

// Condiciones comerciales de las licencias, editables por el administrador: moneda, prueba (días y técnicos incluidos),
// tope de técnicos por club, días de antelación de los avisos de caducidad y los precios de la licencia de club y de cada licencia de técnico, con sus precios de
// Stripe. Una sola fila; sus cambios valen para las altas, pruebas y renovaciones nuevas (lo ya contratado en Stripe
// sigue con su precio hasta que se cambie su renovación).
public sealed class LicensingSettings : AggregateRoot
{
    public static readonly Guid SingletonId = new("01a16f00-0000-7000-8000-000000000001");
    public const string DefaultCurrency = "EUR";

    private readonly List<LicensePriceSettings> _prices = [];

    public string Currency { get; private set; } = DefaultCurrency;
    public int TrialDays { get; private set; }

    // Técnicos que admite la prueba, antes de contratar.
    public int TrialStaffSeats { get; private set; }

    // Licencias de técnico que puede tener un club como mucho.
    public int MaxStaffSeats { get; private set; }

    // Días antes del fin de una licencia que no se renueva en los que se avisa al club, de más a menos (vacío = sin avisos).
    public int[] ExpiryAlertDays { get; private set; } = [];

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<LicensePriceSettings> Prices => _prices.AsReadOnly();

    private LicensingSettings() { }

    // Valores de partida (y los de una base sin la fila sembrada).
    public static LicensingSettings CreateDefault(DateTimeOffset now)
    {
        var settings = new LicensingSettings
        {
            Id = SingletonId, Currency = DefaultCurrency, TrialDays = 30, TrialStaffSeats = 2, MaxStaffSeats = 30, ExpiryAlertDays = [15, 5],
            UpdatedAt = now,
        };
        settings._prices.Add(LicensePriceSettings.Create(SingletonId, LicenseItem.Club, 990, 9900, null, null));
        settings._prices.Add(LicensePriceSettings.Create(SingletonId, LicenseItem.StaffSeat, 250, 2500, null, null));
        return settings;
    }

    public LicensePriceSettings For(LicenseItem item) =>
        _prices.FirstOrDefault(p => p.Item == item) ?? throw new DomainException(Common.Errors.ErrorCode.LicensePlanInvalid);

    public int PriceCents(LicenseItem item, BillingInterval interval) => For(item).PriceCents(interval);

    // Lo que cuesta cada periodo de una suscripción: la licencia de club más sus técnicos.
    public int SubscriptionPriceCents(BillingInterval interval, int staffSeats) =>
        PriceCents(LicenseItem.Club, interval) + staffSeats * PriceCents(LicenseItem.StaffSeat, interval);

    public void EnsureStaffSeatsAllowed(int staffSeats)
    {
        if (staffSeats < 0 || staffSeats > MaxStaffSeats)
            throw new DomainException(Common.Errors.ErrorCode.LicenseStaffSeatsRange, MaxStaffSeats);
    }

    public void Update(
        string currency, int trialDays, int trialStaffSeats, int maxStaffSeats, IReadOnlyCollection<int> expiryAlertDays,
        IReadOnlyCollection<LicensePriceTerms> prices, DateTimeOffset now)
    {
        var code = Guard.NotEmpty(currency, "moneda", 3).ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new DomainException(Common.Errors.ErrorCode.LicensingCurrencyInvalid, currency);

        Guard.InRange(trialDays, 0, 365, "días de prueba");
        Guard.InRange(maxStaffSeats, 1, 200, "técnicos por club");
        Guard.InRange(trialStaffSeats, 0, maxStaffSeats, "técnicos de la prueba");
        var alertDays = expiryAlertDays.Distinct().OrderByDescending(d => d).ToArray();
        Guard.InRange(alertDays.Length, 0, 5, "avisos de caducidad");
        foreach (var days in alertDays)
            Guard.InRange(days, 1, 90, "días de aviso");
        if (prices.Select(p => p.Item).Distinct().Count() != prices.Count || Enum.GetValues<LicenseItem>().Any(i => prices.All(t => t.Item != i)))
            throw new DomainException(Common.Errors.ErrorCode.LicensePlanInvalid);

        // Se validan los dos precios antes de tocar ninguno: un error en el del técnico no deja el del club a medio cambiar.
        var validated = prices.Select(LicensePriceSettings.Validate).ToList();
        foreach (var terms in validated)
            For(terms.Item).Apply(terms);

        Currency = code;
        TrialDays = trialDays;
        TrialStaffSeats = trialStaffSeats;
        MaxStaffSeats = maxStaffSeats;
        ExpiryAlertDays = alertDays;
        UpdatedAt = now;
    }
}

public sealed record LicensePriceTerms(
    LicenseItem Item,
    int MonthlyPriceCents,
    int YearlyPriceCents,
    string? StripeMonthlyPriceId,
    string? StripeYearlyPriceId);

public sealed class LicensePriceSettings : Entity
{
    public Guid SettingsId { get; private set; }
    public LicenseItem Item { get; private set; }
    public int MonthlyPriceCents { get; private set; }
    public int YearlyPriceCents { get; private set; }

    // Precios recurrentes de Stripe (price_...) de cada periodo; sin ellos el pago va con el importe de aquí.
    public string? StripeMonthlyPriceId { get; private set; }
    public string? StripeYearlyPriceId { get; private set; }

    private LicensePriceSettings() { }

    internal static LicensePriceSettings Create(
        Guid settingsId, LicenseItem item, int monthly, int yearly, string? stripeMonthly, string? stripeYearly)
    {
        var settings = new LicensePriceSettings { Id = NewId(), SettingsId = settingsId, Item = item };
        settings.Apply(Validate(new LicensePriceTerms(item, monthly, yearly, stripeMonthly, stripeYearly)));
        return settings;
    }

    public int PriceCents(BillingInterval interval) => interval == BillingInterval.Yearly ? YearlyPriceCents : MonthlyPriceCents;

    public string? StripePriceId(BillingInterval interval) => interval == BillingInterval.Yearly ? StripeYearlyPriceId : StripeMonthlyPriceId;

    internal static LicensePriceTerms Validate(LicensePriceTerms terms) => terms with
    {
        MonthlyPriceCents = Guard.InRange(terms.MonthlyPriceCents, 50, 1_000_000, "precio mensual"),
        YearlyPriceCents = Guard.InRange(terms.YearlyPriceCents, 50, 10_000_000, "precio anual"),
        StripeMonthlyPriceId = StripeId(terms.StripeMonthlyPriceId),
        StripeYearlyPriceId = StripeId(terms.StripeYearlyPriceId),
    };

    internal void Apply(LicensePriceTerms terms)
    {
        MonthlyPriceCents = terms.MonthlyPriceCents;
        YearlyPriceCents = terms.YearlyPriceCents;
        StripeMonthlyPriceId = terms.StripeMonthlyPriceId;
        StripeYearlyPriceId = terms.StripeYearlyPriceId;
    }

    private static string? StripeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var id = Guard.NotEmpty(value, "precio de Stripe", 100);
        if (!id.StartsWith("price_", StringComparison.Ordinal))
            throw new DomainException(Common.Errors.ErrorCode.LicensingStripePriceInvalid, id);
        return id;
    }
}

public interface ILicensingSettingsStore
{
    // Para leer (sin seguimiento de cambios); null si la fila aún no existe.
    Task<LicensingSettings?> GetAsync(CancellationToken cancellationToken = default);
    Task<LicensingSettings?> GetForUpdateAsync(CancellationToken cancellationToken = default);
    void Add(LicensingSettings settings);
}
