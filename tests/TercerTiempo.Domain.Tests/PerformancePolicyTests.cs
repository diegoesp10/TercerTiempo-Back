using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Tests;

public class PerformancePolicyTests
{
    [Fact]
    public void Staff_can_record_a_sheet_line_on_a_played_match()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var match = TestData.PlayedMatch();

        var line = PlayerMatchPerformance.Record(squad, entry, match, coachId, TestData.ValidStats(), TestData.Now);

        Assert.Equal(squad.Id, line.SquadId);
        Assert.Equal(entry.Id, line.SquadPlayerId);
        Assert.Equal(match.Id, line.MatchId);
        Assert.Equal(TestData.HomeTeamId, line.TeamId);
        Assert.Equal(TestData.SeasonId, line.SeasonId);
        Assert.Equal(coachId, line.RecordedByCoachId);
        Assert.Equal(1, line.GoalContributions);
        Assert.Equal(7, line.Rating);
        Assert.Equal(TestData.Now, line.LoggedAt);
    }

    [Fact]
    public void Outsiders_cannot_record_the_sheet()
    {
        var (squad, _) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, entry, TestData.PlayedMatch(), Guid.NewGuid(), TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Former_staff_cannot_record_the_sheet()
    {
        var (squad, _) = TestData.NewSquad();
        var assistant = Guid.NewGuid();
        squad.AddStaff(assistant, Domain.Squads.StaffRole.AssistantCoach, TestData.Now);
        squad.RemoveStaff(assistant, TestData.Now);
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, entry, TestData.PlayedMatch(), assistant, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Cannot_record_on_a_scheduled_match()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, entry, TestData.ScheduledMatch(), coachId, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Cannot_record_for_a_match_the_team_does_not_play()
    {
        var (squad, coachId) = TestData.NewSquad(teamId: Guid.NewGuid());
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, entry, TestData.PlayedMatch(), coachId, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Cannot_record_for_a_match_of_another_season()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var otherSeason = Match.Schedule(Guid.NewGuid(), 1, TestData.HomeTeamId, TestData.AwayTeamId, TestData.Now.AddDays(-3), null, null);
        otherSeason.RecordResult(1, 1, TestData.Now);

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, entry, otherSeason, coachId, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Cannot_record_an_entry_of_another_squad()
    {
        var (squad, coachId) = TestData.NewSquad();
        var (otherSquad, _) = TestData.NewSquad(teamId: TestData.AwayTeamId);
        var foreignEntry = otherSquad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, foreignEntry, TestData.PlayedMatch(), coachId, TestData.ValidStats(), TestData.Now));
    }

    [Fact]
    public void Invalid_stats_are_rejected()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var stats = TestData.ValidStats() with { ShotsOnTarget = 5 };

        Assert.Throws<DomainException>(() =>
            PlayerMatchPerformance.Record(squad, entry, TestData.PlayedMatch(), coachId, stats, TestData.Now));
    }

    [Fact]
    public void Update_replaces_stats_records_the_author_and_bumps_the_timestamp()
    {
        var (squad, coachId) = TestData.NewSquad();
        var assistant = Guid.NewGuid();
        squad.AddStaff(assistant, Domain.Squads.StaffRole.AssistantCoach, TestData.Now);
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var line = PlayerMatchPerformance.Record(squad, entry, TestData.PlayedMatch(), coachId, TestData.ValidStats(), TestData.Now);

        line.Update(TestData.ValidStats(goals: 2, assists: 1, rating: 9), assistant, TestData.Now.AddHours(1));

        Assert.Equal(2, line.Goals);
        Assert.Equal(1, line.Assists);
        Assert.Equal(9, line.Rating);
        Assert.Equal(assistant, line.RecordedByCoachId);
        Assert.Equal(TestData.Now, line.LoggedAt);
        Assert.Equal(TestData.Now.AddHours(1), line.UpdatedAt);
    }

    [Fact]
    public void ToStats_round_trips_the_recorded_values()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var stats = TestData.ValidStats(minutes: 75, goals: 0, assists: 2, rating: 8) with { Notes = "  Gran partido  " };

        var line = PlayerMatchPerformance.Record(squad, entry, TestData.PlayedMatch(), coachId, stats, TestData.Now);

        Assert.Equal(stats with { Notes = "Gran partido" }, line.ToStats());
    }
}
