using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Performances;

namespace TercerTiempo.Domain.Tests;

public class TeamMatchStatsTests
{
    private static TeamMatchStatsData Data(int? possession = 55, int setPieceFor = 1, int shotsFor = 12, int onTargetFor = 5) =>
        new(possession, 6, 3, shotsFor, 7, onTargetFor, 2, 2, 1, 11, 14, setPieceFor, 0, "Buen partido a balón parado");

    [Fact]
    public void The_staff_records_the_team_data_of_a_played_match()
    {
        var (squad, coachId) = TestData.NewSquad();
        var match = TestData.PlayedMatch(home: 2, away: 1);

        var stats = TeamMatchStats.Record(squad, match, coachId, Data(), TestData.Now);

        Assert.Equal((55, 6, 1), (stats.PossessionPct, stats.CornersFor, stats.SetPieceGoalsFor));
        Assert.Equal(coachId, stats.RecordedByCoachId);
    }

    [Fact]
    public void Only_the_staff_can_record_it()
    {
        var (squad, _) = TestData.NewSquad();

        Assert.Throws<DomainException>(() => TeamMatchStats.Record(squad, TestData.PlayedMatch(), Guid.NewGuid(), Data(), TestData.Now));
    }

    [Fact]
    public void Not_before_the_match_is_played()
    {
        var (squad, coachId) = TestData.NewSquad();

        Assert.Throws<DomainException>(() => TeamMatchStats.Record(squad, TestData.ScheduledMatch(), coachId, Data(), TestData.Now));
    }

    [Theory]
    [InlineData(101, 1, 12, 5)]
    [InlineData(50, 3, 12, 5)]
    [InlineData(50, 1, 4, 5)]
    public void Invalid_data_is_rejected(int possession, int setPieceFor, int shotsFor, int onTargetFor)
    {
        var (squad, coachId) = TestData.NewSquad();
        var match = TestData.PlayedMatch(home: 2, away: 1);

        // Posesión > 100, más goles a balón parado (3) que goles del equipo (2) o más tiros a puerta que tiros.
        Assert.ThrowsAny<Exception>(() => TeamMatchStats.Record(squad, match, coachId, Data(possession, setPieceFor, shotsFor, onTargetFor), TestData.Now));
    }

    [Fact]
    public void Updating_overwrites_the_previous_data()
    {
        var (squad, coachId) = TestData.NewSquad();
        var match = TestData.PlayedMatch();
        var stats = TeamMatchStats.Record(squad, match, coachId, Data(), TestData.Now);

        stats.Update(squad, match, coachId, Data(possession: null) with { CornersFor = 9, Notes = "  " }, TestData.Now.AddHours(1));

        Assert.Equal((null, 9, (string?)null), (stats.PossessionPct, stats.CornersFor, stats.Notes));
        Assert.Equal(TestData.Now.AddHours(1), stats.UpdatedAt);
    }
}
