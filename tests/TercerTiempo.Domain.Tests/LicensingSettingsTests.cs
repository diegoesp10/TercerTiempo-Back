using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Domain.Tests;

public class LicensingSettingsTests
{
    private static LicensePlanTerms[] Terms(int basicStaff = 1, int premiumStaff = 6, string? stripe = null) =>
    [
        new(LicensePlan.Basic, 1290, 12900, basicStaff, stripe, null),
        new(LicensePlan.Premium, 2490, 24900, premiumStaff, null, null),
    ];

    [Fact]
    public void Defaults_are_the_launch_prices()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        Assert.Equal(("EUR", 30), (settings.Currency, settings.TrialDays));
        Assert.Equal(990, settings.PriceCents(LicensePlan.Basic, BillingInterval.Monthly));
        Assert.Equal(19900, settings.PriceCents(LicensePlan.Premium, BillingInterval.Yearly));
        Assert.Equal((1, 6), (settings.For(LicensePlan.Basic).MaxStaff, settings.For(LicensePlan.Premium).MaxStaff));
    }

    [Fact]
    public void Update_replaces_prices_trial_and_stripe_ids()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        settings.Update("eur", 15, Terms(stripe: "price_basic_month"), TestData.Now.AddDays(1));

        Assert.Equal(("EUR", 15), (settings.Currency, settings.TrialDays));
        Assert.Equal(1290, settings.PriceCents(LicensePlan.Basic, BillingInterval.Monthly));
        Assert.Equal("price_basic_month", settings.For(LicensePlan.Basic).StripePriceId(BillingInterval.Monthly));
        Assert.Null(settings.For(LicensePlan.Basic).StripePriceId(BillingInterval.Yearly));
        Assert.Equal(TestData.Now.AddDays(1), settings.UpdatedAt);
    }

    [Theory]
    [InlineData("EURO", ErrorCode.CommonTooLong)]
    [InlineData("E1R", ErrorCode.LicensingCurrencyInvalid)]
    public void Currency_must_be_an_iso_code(string currency, string expected)
    {
        var error = Assert.Throws<DomainException>(() => LicensingSettings.CreateDefault(TestData.Now).Update(currency, 30, Terms(), TestData.Now));
        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public void Premium_cannot_admit_fewer_coaches_than_basic_and_stripe_ids_are_checked()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        Assert.Equal(ErrorCode.LicensingPlanOrder, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, Terms(basicStaff: 4, premiumStaff: 2), TestData.Now)).Code);
        Assert.Equal(ErrorCode.LicensingStripePriceInvalid, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, Terms(stripe: "prod_123"), TestData.Now)).Code);
        Assert.Equal(ErrorCode.CommonOutOfRange, Assert.Throws<DomainException>(() => settings.Update("EUR", 400, Terms(), TestData.Now)).Code);
        Assert.Equal(990, settings.PriceCents(LicensePlan.Basic, BillingInterval.Monthly));
    }
}
