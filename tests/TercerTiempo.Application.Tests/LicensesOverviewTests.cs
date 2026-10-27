using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Tests;

public class LicensesOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly LicensingSettings Settings = LicensingSettings.CreateDefault(Now);

    private static AdminLicenseDto Row(ClubLicense license, DateOnly? today = null) =>
        GetLicensesOverviewHandler.ToDto(license, Settings, today ?? Today, new Dictionary<Guid, string>(), new Dictionary<Guid, IReadOnlyList<string>>(), (0, null));

    private static ClubLicense Started(int daysAgo) => ClubLicense.Start(Guid.NewGuid(), Today.AddDays(-daysAgo), 30, Now);

    [Fact]
    public void A_trial_owes_nothing_and_shows_whether_the_license_was_contracted()
    {
        var trial = Started(5);
        Assert.Equal(LicenseBillingState.Trial, Row(trial).State);

        trial.Subscribe(BillingInterval.Monthly, 1, "mock", null, null, Now);
        var row = Row(trial);
        Assert.Equal(LicenseBillingState.TrialWithPlan, row.State);
        Assert.Equal((true, 1), (row.Contracted, row.StaffSeats));
        Assert.Equal(490 + 250, row.NextChargeCents);
        Assert.Equal(0, row.OwedCents);
        Assert.True(row.HasAccess);
    }

    [Fact]
    public void Paid_licenses_are_active_until_the_renewal_date_and_then_owe_it_during_the_grace_days()
    {
        var license = Started(40);
        license.Subscribe(BillingInterval.Yearly, 6, "mock", null, null, Now);
        license.RecordPaidPeriod(Today.AddDays(10), Now);
        Assert.Equal(LicenseBillingState.Active, Row(license).State);

        var grace = Row(license, Today.AddDays(12));
        Assert.Equal(LicenseBillingState.Grace, grace.State);
        Assert.True(grace.HasAccess);
        Assert.Equal(19900, grace.OwedCents);
        Assert.Equal(Today.AddDays(11), grace.OwedSince);

        var unpaid = Row(license, Today.AddDays(20));
        Assert.Equal(LicenseBillingState.Unpaid, unpaid.State);
        Assert.False(unpaid.HasAccess);
        Assert.Equal(19900, unpaid.OwedCents);
    }

    [Fact]
    public void A_cancelled_renewal_or_an_ended_trial_loses_access_without_debt()
    {
        var cancelled = Started(40);
        cancelled.Subscribe(BillingInterval.Monthly, 0, "mock", null, null, Now);
        cancelled.RecordPaidPeriod(Today.AddDays(-5), Now);
        cancelled.CancelRenewal(Now);
        var cancelledRow = Row(cancelled);
        Assert.Equal(LicenseBillingState.Cancelled, cancelledRow.State);
        Assert.False(cancelledRow.HasAccess);
        Assert.Equal(0, cancelledRow.OwedCents);

        var ended = Row(Started(40));
        Assert.Equal(LicenseBillingState.TrialEnded, ended.State);
        Assert.False(ended.HasAccess);
        Assert.Equal(0, ended.OwedCents);
    }
}
