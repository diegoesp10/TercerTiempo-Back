using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Domain.Tests;

public class ClubLicenseTests
{
    private static readonly DateOnly Linked = TestData.Today;

    private static ClubLicense NewLicense() => ClubLicense.Start(Guid.NewGuid(), Linked, 30, TestData.Now);

    [Fact]
    public void The_trial_admits_its_coaches_until_the_club_license_is_contracted()
    {
        var license = NewLicense();

        Assert.Equal(LicenseStatus.Trial, license.StatusOn(Linked.AddDays(30)));
        Assert.Equal(LicenseStatus.Expired, license.StatusOn(Linked.AddDays(31)));
        Assert.False(license.IsContracted);
        Assert.Equal(2, license.StaffLimit(trialStaffSeats: 2));
        Assert.Null(license.RenewsOn);
    }

    [Fact]
    public void Contracting_during_the_trial_charges_nothing_until_it_ends_and_keeps_the_chosen_coaches()
    {
        var license = NewLicense();

        license.Subscribe(BillingInterval.Monthly, 0, "mock", "mocksub_1", null, TestData.Now);

        Assert.Null(license.PaidUntil);
        Assert.True(license.IsContracted);
        Assert.Equal(0, license.StaffLimit(trialStaffSeats: 2));
        Assert.Equal(Linked.AddDays(31), license.RenewsOn);
        Assert.Equal(LicenseStatus.Trial, license.StatusOn(Linked.AddDays(30)));
    }

    [Fact]
    public void A_renewing_subscription_keeps_access_for_a_few_days_while_it_is_charged()
    {
        var license = NewLicense();
        license.Subscribe(BillingInterval.Monthly, 1, "mock", null, null, TestData.Now);
        var trialEnd = license.TrialEndsOn;

        Assert.Equal(LicenseStatus.Active, license.StatusOn(trialEnd.AddDays(ClubLicense.RenewalGraceDays)));
        Assert.Equal(LicenseStatus.Expired, license.StatusOn(trialEnd.AddDays(ClubLicense.RenewalGraceDays + 1)));

        license.CancelRenewal(TestData.Now);
        Assert.Equal(LicenseStatus.Expired, license.StatusOn(trialEnd.AddDays(1)));
    }

    [Fact]
    public void Renewals_follow_on_without_gaps_and_apply_the_requested_changes()
    {
        var license = NewLicense();
        license.Subscribe(BillingInterval.Monthly, 3, "mock", null, null, TestData.Now);
        var trialEnd = license.TrialEndsOn;

        license.RecordRenewal(license.RenewalPeriodEnd(BillingInterval.Monthly, trialEnd.AddDays(1)), TestData.Now);
        Assert.Equal(trialEnd.AddMonths(1), license.PaidUntil);

        var today = trialEnd.AddDays(5);
        license.ChangeRenewal(BillingInterval.Yearly, today, TestData.Now);
        license.ChangeStaffSeats(1, today, TestData.Now);
        Assert.Equal((BillingInterval.Monthly, 3), (license.Interval, license.StaffSeats));
        Assert.Equal((BillingInterval.Yearly, 1), (license.NextInterval, license.NextStaffSeats));
        // Con la reducción pedida, los nombramientos ya no pueden pasar de lo que se pagará en la renovación.
        Assert.Equal(1, license.StaffLimit(trialStaffSeats: 2));

        license.RecordRenewal(license.RenewalPeriodEnd(BillingInterval.Yearly, license.ValidUntil.AddDays(1)), TestData.Now);
        Assert.Equal((BillingInterval.Yearly, 1), (license.Interval, license.StaffSeats));
        Assert.Null(license.NextInterval);
        Assert.Null(license.NextStaffSeats);
        Assert.Equal(trialEnd.AddMonths(1).AddMonths(12), license.PaidUntil);
    }

    [Fact]
    public void Changes_during_the_trial_apply_at_once()
    {
        var license = NewLicense();
        license.Subscribe(BillingInterval.Monthly, 4, "mock", null, null, TestData.Now);

        license.ChangeRenewal(BillingInterval.Yearly, Linked.AddDays(3), TestData.Now);
        var added = license.ChangeStaffSeats(2, Linked.AddDays(3), TestData.Now);

        Assert.Equal(0, added);
        Assert.Equal((BillingInterval.Yearly, 2), (license.Interval, license.StaffSeats));
        Assert.Null(license.NextInterval);
        Assert.Null(license.NextStaffSeats);
        // Durante la prueba no hay nada pagado: añadir técnicos no cobra nada hasta el primer cobro.
        Assert.Equal(0, license.StaffSeatsProrationCents(license.ChangeStaffSeats(5, Linked.AddDays(3), TestData.Now), 2500, Linked.AddDays(3)));
        Assert.Equal(5, license.StaffSeats);
    }

    [Fact]
    public void Adding_coaches_in_a_paid_period_applies_at_once_and_charges_the_rest_of_the_period()
    {
        var license = NewLicense();
        license.Subscribe(BillingInterval.Monthly, 1, "mock", null, null, TestData.Now);
        var periodEnd = Linked.AddDays(30).AddMonths(1);
        license.RecordPaidPeriod(periodEnd, TestData.Now);
        var periodDays = periodEnd.DayNumber - periodEnd.AddMonths(-1).DayNumber;
        var today = periodEnd.AddDays(-9);

        var added = license.ChangeStaffSeats(3, today, TestData.Now);

        Assert.Equal(2, added);
        Assert.Equal(3, license.StaffSeats);
        Assert.Equal(3, license.StaffLimit(trialStaffSeats: 2));
        // Dos técnicos a 2,50 € durante los 10 días que quedan (hoy incluido).
        Assert.Equal((int)Math.Round(2 * 250m * 10 / periodDays, MidpointRounding.AwayFromZero), license.StaffSeatsProrationCents(added, 250, today));
    }

    [Fact]
    public void Coaches_are_only_bought_with_the_club_license_contracted_and_renewing()
    {
        var license = NewLicense();

        Assert.Equal(ErrorCode.LicenseClubRequired, Assert.Throws<DomainException>(() => license.ChangeStaffSeats(2, Linked, TestData.Now)).Code);

        license.Subscribe(BillingInterval.Monthly, 0, "mock", null, null, TestData.Now);
        license.CancelRenewal(TestData.Now);
        Assert.Equal(ErrorCode.LicenseNoSubscription, Assert.Throws<DomainException>(() => license.ChangeStaffSeats(2, Linked, TestData.Now)).Code);
    }

    [Fact]
    public void Repeated_payment_notices_never_shorten_the_paid_period()
    {
        var license = NewLicense();
        license.RecordPaidPeriod(Linked.AddDays(90), TestData.Now);
        license.RecordPaidPeriod(Linked.AddDays(60), TestData.Now);

        Assert.Equal(Linked.AddDays(90), license.PaidUntil);
    }

    [Fact]
    public void Without_a_subscription_there_is_nothing_to_cancel_or_change()
    {
        var license = NewLicense();

        Assert.Throws<DomainException>(() => license.CancelRenewal(TestData.Now));
        Assert.Throws<DomainException>(() => license.ChangeRenewal(BillingInterval.Monthly, Linked, TestData.Now));
        Assert.Throws<DomainException>(() => license.ResumeRenewal(Linked, TestData.Now));
    }

    [Fact]
    public void Payments_record_the_period_price_the_coaches_and_the_months_of_the_interval()
    {
        var license = NewLicense();
        var payment = LicensePayment.Begin(license, LicensePaymentKind.Subscription, BillingInterval.Yearly, 6, 0, 19900, "EUR", "mock", "mock_1", TestData.Now);

        Assert.Equal((12, 6), (payment.Months, payment.StaffSeats));
        Assert.Equal(0, payment.AmountCents);
        Assert.Equal(19900, payment.PeriodPriceCents);
        payment.MarkPaid(TestData.Now);
        Assert.Throws<DomainException>(() => payment.MarkPaid(TestData.Now));
    }
}
