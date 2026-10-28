using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Domain.Tests;

public class LicensingSettingsTests
{
    private static LicensePriceTerms[] Prices(string? stripe = null) =>
    [
        new(LicenseItem.Club, 590, 5900, stripe, null),
        new(LicenseItem.StaffSeat, 290, 2900, null, null),
    ];

    [Fact]
    public void Defaults_are_a_cheap_club_license_plus_paid_coaches()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        Assert.Equal(("EUR", 30, 2, 30), (settings.Currency, settings.TrialDays, settings.TrialStaffSeats, settings.MaxStaffSeats));
        Assert.Equal([15, 5], settings.ExpiryAlertDays);
        Assert.Equal(990, settings.PriceCents(LicenseItem.Club, BillingInterval.Monthly));
        Assert.Equal(2500, settings.PriceCents(LicenseItem.StaffSeat, BillingInterval.Yearly));
        // El club con 6 técnicos: 9,90 € + 6 × 2,50 € al mes.
        Assert.Equal(2490, settings.SubscriptionPriceCents(BillingInterval.Monthly, 6));
        Assert.Equal(24900, settings.SubscriptionPriceCents(BillingInterval.Yearly, 6));
        Assert.Equal(9900, settings.SubscriptionPriceCents(BillingInterval.Yearly, 0));
    }

    [Fact]
    public void Update_replaces_prices_trial_seats_and_stripe_ids()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        settings.Update("eur", 15, 1, 10, [15, 5], Prices(stripe: "price_club_month"), TestData.Now.AddDays(1));

        Assert.Equal(("EUR", 15, 1, 10), (settings.Currency, settings.TrialDays, settings.TrialStaffSeats, settings.MaxStaffSeats));
        Assert.Equal(590 + 2 * 290, settings.SubscriptionPriceCents(BillingInterval.Monthly, 2));
        Assert.Equal("price_club_month", settings.For(LicenseItem.Club).StripePriceId(BillingInterval.Monthly));
        Assert.Null(settings.For(LicenseItem.Club).StripePriceId(BillingInterval.Yearly));
        Assert.Equal(TestData.Now.AddDays(1), settings.UpdatedAt);
    }

    [Theory]
    [InlineData("EURO", ErrorCode.CommonTooLong)]
    [InlineData("E1R", ErrorCode.LicensingCurrencyInvalid)]
    public void Currency_must_be_an_iso_code(string currency, string expected)
    {
        var error = Assert.Throws<DomainException>(() => LicensingSettings.CreateDefault(TestData.Now).Update(currency, 30, 2, 30, [15, 5], Prices(), TestData.Now));
        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public void Invalid_terms_change_nothing()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        Assert.Equal(ErrorCode.LicensingStripePriceInvalid, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, 2, 30, [15, 5], Prices(stripe: "prod_123"), TestData.Now)).Code);
        Assert.Equal(ErrorCode.CommonOutOfRange, Assert.Throws<DomainException>(() => settings.Update("EUR", 400, 2, 30, [15, 5], Prices(), TestData.Now)).Code);
        Assert.Equal(ErrorCode.CommonOutOfRange, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, 5, 3, [15, 5], Prices(), TestData.Now)).Code);
        Assert.Equal(ErrorCode.LicensePlanInvalid, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, 2, 30, [15, 5], [Prices()[0]], TestData.Now)).Code);
        Assert.Equal(990, settings.PriceCents(LicenseItem.Club, BillingInterval.Monthly));
    }

    [Fact]
    public void Expiry_alert_days_are_sorted_without_repeats_and_can_be_turned_off()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        settings.Update("EUR", 30, 2, 30, [5, 30, 5, 10], Prices(), TestData.Now);
        Assert.Equal([30, 10, 5], settings.ExpiryAlertDays);

        Assert.Equal(ErrorCode.CommonOutOfRange, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, 2, 30, [0], Prices(), TestData.Now)).Code);
        Assert.Equal(ErrorCode.CommonOutOfRange, Assert.Throws<DomainException>(() => settings.Update("EUR", 30, 2, 30, [1, 2, 3, 4, 5, 6], Prices(), TestData.Now)).Code);

        settings.Update("EUR", 30, 2, 30, [], Prices(), TestData.Now);
        Assert.Empty(settings.ExpiryAlertDays);
    }

    [Fact]
    public void A_club_cannot_go_over_the_staff_seat_cap()
    {
        var settings = LicensingSettings.CreateDefault(TestData.Now);

        settings.EnsureStaffSeatsAllowed(0);
        settings.EnsureStaffSeatsAllowed(30);
        Assert.Equal(ErrorCode.LicenseStaffSeatsRange, Assert.Throws<DomainException>(() => settings.EnsureStaffSeatsAllowed(31)).Code);
    }
}
