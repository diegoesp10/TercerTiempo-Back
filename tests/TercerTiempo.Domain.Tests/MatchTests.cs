using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Domain.Tests;

public class MatchTests
{
    [Fact]
    public void Team_cannot_play_itself()
    {
        Assert.Throws<DomainException>(() =>
            Match.Schedule(TestData.SeasonId, 1, TestData.HomeTeamId, TestData.HomeTeamId, TestData.Now, null, null));
    }

    [Fact]
    public void Record_result_marks_match_as_played_and_raises_event_once()
    {
        var match = TestData.ScheduledMatch();

        match.RecordResult(3, 1, TestData.Now);
        match.RecordResult(3, 1, TestData.Now.AddMinutes(5));

        Assert.Equal(MatchStatus.Played, match.Status);
        Assert.Equal("3-1", match.Score?.ToString());
        Assert.Single(match.DomainEvents);
    }

    [Fact]
    public void Outcome_and_points_are_relative_to_team()
    {
        var match = TestData.PlayedMatch(home: 2, away: 1);

        Assert.Equal(MatchOutcome.Win, match.OutcomeFor(TestData.HomeTeamId));
        Assert.Equal(MatchOutcome.Loss, match.OutcomeFor(TestData.AwayTeamId));
        Assert.Equal(3, match.PointsFor(TestData.HomeTeamId));
        Assert.Equal(0, match.PointsFor(TestData.AwayTeamId));
        Assert.Equal(1, match.GoalsFor(TestData.AwayTeamId));
        Assert.Equal(2, match.GoalsAgainst(TestData.AwayTeamId));
    }

    [Fact]
    public void Scheduled_match_has_no_outcome()
    {
        var match = TestData.ScheduledMatch();

        Assert.Equal(MatchOutcome.NotPlayed, match.OutcomeFor(TestData.HomeTeamId));
        Assert.Null(match.PointsFor(TestData.HomeTeamId));
        Assert.Null(match.Score);
    }

    [Fact]
    public void Played_match_cannot_be_rescheduled_or_cancelled()
    {
        var match = TestData.PlayedMatch();

        Assert.Throws<DomainException>(() => match.Reschedule(TestData.Now.AddDays(10)));
        Assert.Throws<DomainException>(match.Cancel);
        Assert.Throws<DomainException>(match.Postpone);
    }

    [Fact]
    public void Cancelled_match_cannot_receive_result()
    {
        var match = TestData.ScheduledMatch();
        match.Cancel();

        Assert.Throws<DomainException>(() => match.RecordResult(1, 0, TestData.Now));
    }

    [Fact]
    public void Opponent_is_resolved_for_both_sides()
    {
        var match = TestData.ScheduledMatch();

        Assert.Equal(TestData.AwayTeamId, match.OpponentOf(TestData.HomeTeamId));
        Assert.Equal(TestData.HomeTeamId, match.OpponentOf(TestData.AwayTeamId));
        Assert.Throws<DomainException>(() => match.OpponentOf(Guid.NewGuid()));
    }
}
