using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Domain.Tests;

public class FootballSyncSettingsTests
{
    [Fact]
    public void By_default_the_round_spans_three_days_back_and_four_ahead_and_the_season_is_redone_weekly()
    {
        var settings = FootballSyncSettings.CreateDefault(TestData.Now);

        Assert.Equal((new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 28)), settings.RoundWindow(new DateOnly(2026, 10, 24)));
        Assert.True(settings.FullSyncDue(null, TestData.Now));
        Assert.False(settings.FullSyncDue(TestData.Now.AddDays(-6), TestData.Now));
        Assert.True(settings.FullSyncDue(TestData.Now.AddDays(-7), TestData.Now));
    }

    [Fact]
    public void The_automatic_run_cannot_be_a_fixed_date_range_and_days_are_bounded()
    {
        var settings = FootballSyncSettings.CreateDefault(TestData.Now);

        Assert.Equal(ErrorCode.SyncScheduledScopeInvalid,
            Assert.Throws<DomainException>(() => settings.Update(SyncScopeKind.DateRange, 3, 4, false, 7, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CommonOutOfRange,
            Assert.Throws<DomainException>(() => settings.Update(SyncScopeKind.CurrentRound, 15, 4, false, 7, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CommonOutOfRange,
            Assert.Throws<DomainException>(() => settings.Update(SyncScopeKind.CurrentRound, 3, 4, false, 61, TestData.Now)).Code);

        settings.Update(SyncScopeKind.CurrentRound, 0, 0, true, 0, TestData.Now);
        Assert.False(settings.FullSyncDue(null, TestData.Now));
        Assert.Equal((new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 24)), settings.RoundWindow(new DateOnly(2026, 10, 24)));
    }
}
