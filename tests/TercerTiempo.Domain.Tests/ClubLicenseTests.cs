using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Domain.Tests;

public class ClubLicenseTests
{
    private static readonly DateOnly Linked = TestData.Today;

    private static ClubLicense NewLicense() => ClubLicense.Start(Guid.NewGuid(), Linked, 30, TestData.Now);

    [Fact]
    public void Trial_works_as_premium_until_a_plan_is_chosen()
    {
        var license = NewLicense();

        Assert.Equal(LicenseStatus.Trial, license.StatusOn(Linked.AddDays(30)));
        Assert.Equal(LicenseStatus.Expired, license.StatusOn(Linked.AddDays(31)));
        Assert.Equal(LicensePlan.Premium, license.EffectivePlan);
        Assert.Null(license.RenewsOn);
    }

    [Fact]
    public void Subscribing_during_the_trial_charges_nothing_until_it_ends()
    {
        var license = NewLicense();

        license.Subscribe(LicensePlan.Basic, BillingInterval.Monthly, "mock", "mocksub_1", null, TestData.Now);

        Assert.Null(license.PaidUntil);
        Assert.Equal(LicensePlan.Basic, license.EffectivePlan);
        Assert.Equal(Linked.AddDays(31), license.RenewsOn);
        Assert.Equal(LicenseStatus.Trial, license.StatusOn(Linked.AddDays(30)));
    }

    [Fact]
    public void A_renewing_subscription_keeps_access_for_a_few_days_while_it_is_charged()
    {
        var license = NewLicense();
        license.Subscribe(LicensePlan.Basic, BillingInterval.Monthly, "mock", null, null, TestData.Now);
        var trialEnd = license.TrialEndsOn;

        Assert.Equal(LicenseStatus.Active, license.StatusOn(trialEnd.AddDays(ClubLicense.RenewalGraceDays)));
        Assert.Equal(LicenseStatus.Expired, license.StatusOn(trialEnd.AddDays(ClubLicense.RenewalGraceDays + 1)));

        license.CancelRenewal(TestData.Now);
        Assert.Equal(LicenseStatus.Expired, license.StatusOn(trialEnd.AddDays(1)));
    }

    [Fact]
    public void Renewals_follow_on_without_gaps_and_apply_the_requested_change()
    {
        var license = NewLicense();
        license.Subscribe(LicensePlan.Basic, BillingInterval.Monthly, "mock", null, null, TestData.Now);
        var trialEnd = license.TrialEndsOn;

        license.RecordRenewal(license.RenewalPeriodEnd(BillingInterval.Monthly, trialEnd.AddDays(1)), TestData.Now);
        Assert.Equal(trialEnd.AddMonths(1), license.PaidUntil);

        var today = trialEnd.AddDays(5);
        license.ChangeRenewal(LicensePlan.Premium, BillingInterval.Yearly, today, TestData.Now);
        Assert.Equal(LicensePlan.Basic, license.Plan);
        Assert.Equal(LicensePlan.Premium, license.NextPlan);

        license.RecordRenewal(license.RenewalPeriodEnd(BillingInterval.Yearly, license.ValidUntil.AddDays(1)), TestData.Now);
        Assert.Equal(LicensePlan.Premium, license.Plan);
        Assert.Equal(BillingInterval.Yearly, license.Interval);
        Assert.Null(license.NextPlan);
        Assert.Equal(trialEnd.AddMonths(1).AddMonths(12), license.PaidUntil);
    }

    [Fact]
    public void Changing_the_plan_during_the_trial_applies_at_once()
    {
        var license = NewLicense();
        license.Subscribe(LicensePlan.Premium, BillingInterval.Monthly, "mock", null, null, TestData.Now);

        license.ChangeRenewal(LicensePlan.Basic, BillingInterval.Yearly, Linked.AddDays(3), TestData.Now);

        Assert.Equal(LicensePlan.Basic, license.Plan);
        Assert.Equal(BillingInterval.Yearly, license.Interval);
        Assert.Null(license.NextPlan);
    }

    [Fact]
    public void The_staff_limit_uses_the_smaller_plan_when_a_downgrade_is_pending()
    {
        var license = NewLicense();
        license.Subscribe(LicensePlan.Premium, BillingInterval.Monthly, "mock", null, null, TestData.Now);
        license.RecordPaidPeriod(Linked.AddDays(60), TestData.Now);

        license.ChangeRenewal(LicensePlan.Basic, BillingInterval.Monthly, Linked.AddDays(40), TestData.Now);

        Assert.Equal(LicensePlan.Premium, license.EffectivePlan);
        Assert.Equal(LicensePlan.Basic, license.StaffPlan);
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
        Assert.Throws<DomainException>(() => license.ChangeRenewal(LicensePlan.Basic, BillingInterval.Monthly, Linked, TestData.Now));
        Assert.Throws<DomainException>(() => license.ResumeRenewal(Linked, TestData.Now));
    }

    [Fact]
    public void Payments_record_the_period_price_and_the_months_of_the_interval()
    {
        var license = NewLicense();
        var payment = LicensePayment.Begin(license, LicensePaymentKind.Subscription, LicensePlan.Premium, BillingInterval.Yearly, 0, 19900, "EUR", "mock", "mock_1", TestData.Now);

        Assert.Equal(12, payment.Months);
        Assert.Equal(0, payment.AmountCents);
        Assert.Equal(19900, payment.PeriodPriceCents);
        payment.MarkPaid(TestData.Now);
        Assert.Throws<DomainException>(() => payment.MarkPaid(TestData.Now));
    }
}
