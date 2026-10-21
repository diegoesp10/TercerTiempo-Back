using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Domain.Tests;

public class PerformanceStatsTests
{
    [Fact]
    public void Valid_stats_pass_validation()
    {
        var stats = TestData.ValidStats();

        var exception = Record.Exception(stats.EnsureValid);

        Assert.Null(exception);
    }

    [Fact]
    public void Shots_on_target_cannot_exceed_shots()
    {
        var stats = TestData.ValidStats() with { Shots = 1, ShotsOnTarget = 3 };

        Assert.Throws<DomainException>(stats.EnsureValid);
    }

    [Fact]
    public void Cannot_log_actions_without_minutes()
    {
        var stats = TestData.ValidStats(minutes: 0, goals: 1);

        Assert.Throws<DomainException>(stats.EnsureValid);
    }

    [Fact]
    public void Unused_substitute_with_zero_minutes_is_valid()
    {
        var stats = new PerformanceStats(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, "No jugué");

        var exception = Record.Exception(stats.EnsureValid);

        Assert.Null(exception);
    }

    [Fact]
    public void Two_yellow_cards_require_red_card()
    {
        var stats = TestData.ValidStats() with { YellowCards = 2, RedCard = false };

        Assert.Throws<DomainException>(stats.EnsureValid);
    }

    [Fact]
    public void Minutes_cannot_exceed_120()
    {
        var stats = TestData.ValidStats(minutes: 121);

        Assert.Throws<DomainException>(stats.EnsureValid);
    }
}
