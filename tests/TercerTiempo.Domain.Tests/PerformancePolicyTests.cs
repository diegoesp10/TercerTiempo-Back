using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Domain.Tests;

public class PerformancePolicyTests
{
    [Fact]
    public void Active_squad_member_can_log_performance_on_played_match()
    {
        var player = TestData.NewPlayer();
        player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, 9, verifiedInRoster: true, TestData.Now);
        var match = TestData.PlayedMatch();

        var performance = PlayerMatchPerformance.Log(player, match, TestData.HomeTeamId, TestData.ValidStats(), TestData.Now);

        Assert.Equal(player.Id, performance.PlayerId);
        Assert.Equal(match.Id, performance.MatchId);
        Assert.Equal(TestData.SeasonId, performance.SeasonId);
        Assert.Equal(1, performance.GoalContributions);
    }

    [Fact]
    public void Pending_membership_cannot_log_performance()
    {
        var player = TestData.NewPlayer();
        player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, 9, verifiedInRoster: false, TestData.Now);
        var match = TestData.PlayedMatch();

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Log(player, match, TestData.HomeTeamId, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Cannot_log_performance_on_scheduled_match()
    {
        var player = TestData.NewPlayer();
        player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, 9, verifiedInRoster: true, TestData.Now);
        var match = TestData.ScheduledMatch();

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Log(player, match, TestData.HomeTeamId, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Cannot_log_performance_for_team_not_in_match()
    {
        var player = TestData.NewPlayer();
        var otherTeam = Guid.NewGuid();
        player.JoinSquad(otherTeam, TestData.SeasonId, 9, verifiedInRoster: true, TestData.Now);
        var match = TestData.PlayedMatch();

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Log(player, match, otherTeam, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Update_replaces_stats_and_bumps_timestamp()
    {
        var player = TestData.NewPlayer();
        player.JoinSquad(TestData.HomeTeamId, TestData.SeasonId, 9, verifiedInRoster: true, TestData.Now);
        var performance = PlayerMatchPerformance.Log(player, TestData.PlayedMatch(), TestData.HomeTeamId, TestData.ValidStats(), TestData.Now);

        performance.Update(TestData.ValidStats(goals: 2, assists: 1, rating: 9), TestData.Now.AddHours(1));

        Assert.Equal(2, performance.Goals);
        Assert.Equal(1, performance.Assists);
        Assert.Equal(9, performance.SelfRating);
        Assert.Equal(TestData.Now.AddHours(1), performance.UpdatedAt);
    }
}
