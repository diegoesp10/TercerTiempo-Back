using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Licensing;

// Condiciones comerciales de las licencias, editables por el administrador: moneda, días de prueba y, por plan, precios,
// técnicos admitidos y precios de Stripe. Una sola fila; sus cambios valen para las altas, pruebas y renovaciones
// nuevas (lo ya contratado en Stripe sigue con su precio hasta que se cambie su renovación).
public sealed class LicensingSettings : AggregateRoot
{
    public static readonly Guid SingletonId = new("01a16f00-0000-7000-8000-000000000001");
    public const string DefaultCurrency = "EUR";

    private readonly List<LicensePlanSettings> _plans = [];

    public string Currency { get; private set; } = DefaultCurrency;
    public int TrialDays { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<LicensePlanSettings> Plans => _plans.AsReadOnly();

    private LicensingSettings() { }

    // Valores con los que se lanzó la app (y los de una base sin la fila sembrada).
    public static LicensingSettings CreateDefault(DateTimeOffset now)
    {
        var settings = new LicensingSettings { Id = SingletonId, Currency = DefaultCurrency, TrialDays = 30, UpdatedAt = now };
        settings._plans.Add(LicensePlanSettings.Create(SingletonId, LicensePlan.Basic, 990, 9900, 1, null, null));
        settings._plans.Add(LicensePlanSettings.Create(SingletonId, LicensePlan.Premium, 1990, 19900, 6, null, null));
        return settings;
    }

    public LicensePlanSettings For(LicensePlan plan) =>
        _plans.FirstOrDefault(p => p.Plan == plan) ?? throw new DomainException(Common.Errors.ErrorCode.LicensePlanInvalid);

    public int PriceCents(LicensePlan plan, BillingInterval interval) => For(plan).PriceCents(interval);

    public void Update(string currency, int trialDays, IReadOnlyCollection<LicensePlanTerms> plans, DateTimeOffset now)
    {
        var code = Guard.NotEmpty(currency, "moneda", 3).ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            throw new DomainException(Common.Errors.ErrorCode.LicensingCurrencyInvalid, currency);

        Guard.InRange(trialDays, 0, 365, "días de prueba");
        if (plans.Select(p => p.Plan).Distinct().Count() != plans.Count || Enum.GetValues<LicensePlan>().Any(p => plans.All(t => t.Plan != p)))
            throw new DomainException(Common.Errors.ErrorCode.LicensePlanInvalid);

        // Premium nunca admite menos técnicos que Básica: el límite al cambiar de plan depende de ese orden.
        var basic = plans.Single(p => p.Plan == LicensePlan.Basic);
        var premium = plans.Single(p => p.Plan == LicensePlan.Premium);
        if (premium.MaxStaff < basic.MaxStaff)
            throw new DomainException(Common.Errors.ErrorCode.LicensingPlanOrder);

        // Se validan los dos planes antes de tocar ninguno: un error en Premium no deja Básica a medio cambiar.
        var validated = plans.Select(LicensePlanSettings.Validate).ToList();
        foreach (var terms in validated)
            For(terms.Plan).Apply(terms);

        Currency = code;
        TrialDays = trialDays;
        UpdatedAt = now;
    }
}

public sealed record LicensePlanTerms(
    LicensePlan Plan,
    int MonthlyPriceCents,
    int YearlyPriceCents,
    int MaxStaff,
    string? StripeMonthlyPriceId,
    string? StripeYearlyPriceId);

public sealed class LicensePlanSettings : Entity
{
    public Guid SettingsId { get; private set; }
    public LicensePlan Plan { get; private set; }
    public int MonthlyPriceCents { get; private set; }
    public int YearlyPriceCents { get; private set; }

    // Técnicos (nombramientos vigentes o pendientes de canjear) que admite el equipo en la temporada.
    public int MaxStaff { get; private set; }

    // Precios recurrentes de Stripe (price_...) de cada periodo; sin ellos el pago va con el importe de aquí.
    public string? StripeMonthlyPriceId { get; private set; }
    public string? StripeYearlyPriceId { get; private set; }

    private LicensePlanSettings() { }

    internal static LicensePlanSettings Create(
        Guid settingsId, LicensePlan plan, int monthly, int yearly, int maxStaff, string? stripeMonthly, string? stripeYearly)
    {
        var settings = new LicensePlanSettings { Id = NewId(), SettingsId = settingsId, Plan = plan };
        settings.Apply(Validate(new LicensePlanTerms(plan, monthly, yearly, maxStaff, stripeMonthly, stripeYearly)));
        return settings;
    }

    public int PriceCents(BillingInterval interval) => interval == BillingInterval.Yearly ? YearlyPriceCents : MonthlyPriceCents;

    public string? StripePriceId(BillingInterval interval) => interval == BillingInterval.Yearly ? StripeYearlyPriceId : StripeMonthlyPriceId;

    internal static LicensePlanTerms Validate(LicensePlanTerms terms) => terms with
    {
        MonthlyPriceCents = Guard.InRange(terms.MonthlyPriceCents, 50, 1_000_000, "precio mensual"),
        YearlyPriceCents = Guard.InRange(terms.YearlyPriceCents, 50, 10_000_000, "precio anual"),
        MaxStaff = Guard.InRange(terms.MaxStaff, 1, 50, "técnicos"),
        StripeMonthlyPriceId = StripeId(terms.StripeMonthlyPriceId),
        StripeYearlyPriceId = StripeId(terms.StripeYearlyPriceId),
    };

    internal void Apply(LicensePlanTerms terms)
    {
        MonthlyPriceCents = terms.MonthlyPriceCents;
        YearlyPriceCents = terms.YearlyPriceCents;
        MaxStaff = terms.MaxStaff;
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
