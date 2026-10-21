using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Tests;

public class PerformanceRatingTests
{
    private static PerformanceStats Line(
        int minutes = 90, int goals = 0, int assists = 0, int shots = 0, int onTarget = 0, int keyPasses = 0,
        int tackles = 0, int interceptions = 0, int saves = 0, int yellows = 0, bool red = false) =>
        new(true, minutes, goals, assists, shots, onTarget, keyPasses, tackles, interceptions, saves, yellows, red, null);

    [Fact]
    public void No_minutes_means_no_rating()
    {
        var stats = new PerformanceStats(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, null);

        Assert.Null(PerformanceRating.Calculate(stats, Position.Forward, 1, 0));
    }

    [Fact]
    public void Breakdown_adds_each_block_to_the_base()
    {
        // Delantero, 90', 1 gol, 3 tiros (2 a puerta), 1 pase clave, 2 entradas, 1 intercepción, victoria 2-1.
        var rating = PerformanceRating.Calculate(
            Line(goals: 1, shots: 3, onTarget: 2, keyPasses: 1, tackles: 2, interceptions: 1), Position.Forward, 2, 1)!;

        Assert.Equal(1.2, rating.Attack);
        Assert.Equal(0.15, rating.Creation);
        Assert.Equal(0.28, rating.Defense);
        Assert.Equal(0, rating.Goalkeeping);
        Assert.Equal(0, rating.Discipline);
        Assert.Equal(0.4, rating.Result);
        Assert.Equal(8.0, rating.Total);
    }

    [Fact]
    public void A_goal_is_worth_more_the_further_back_the_player_plays()
    {
        var forward = PerformanceRating.Calculate(Line(goals: 1), Position.Forward, 1, 1)!.Attack;
        var midfielder = PerformanceRating.Calculate(Line(goals: 1), Position.Midfielder, 1, 1)!.Attack;
        var defender = PerformanceRating.Calculate(Line(goals: 1), Position.Defender, 1, 1)!.Attack;

        Assert.True(forward < midfielder && midfielder < defender);
    }

    [Fact]
    public void Goalkeeper_is_rewarded_for_saves_and_a_clean_sheet()
    {
        var cleanSheet = PerformanceRating.Calculate(Line(saves: 4), Position.Goalkeeper, 1, 0)!;
        var conceded = PerformanceRating.Calculate(Line(saves: 4), Position.Goalkeeper, 1, 3)!;

        Assert.Equal(1.4 + 1.0, cleanSheet.Goalkeeping, 2);   // 4 paradas + portería a cero
        Assert.Equal(1.4 - 0.9, conceded.Goalkeeping, 2);     // 4 paradas − 3 goles encajados
        Assert.Equal(0, cleanSheet.Defense);
        Assert.True(cleanSheet.Total > conceded.Total);
    }

    [Fact]
    public void Clean_sheet_needs_at_least_60_minutes()
    {
        var short_ = PerformanceRating.Calculate(Line(minutes: 45), Position.Defender, 1, 0)!;
        var full = PerformanceRating.Calculate(Line(minutes: 60), Position.Defender, 1, 0)!;

        Assert.Equal(0, short_.Defense);
        Assert.Equal(0.6, full.Defense);
    }

    [Fact]
    public void Result_is_proportional_to_minutes_played()
    {
        var sub = PerformanceRating.Calculate(Line(minutes: 30), Position.Forward, 0, 2)!;

        Assert.Equal(-0.1, sub.Result);      // derrota (−0,3) × 30/90
        Assert.Equal(5.9, sub.Total);
    }

    [Fact]
    public void A_red_card_replaces_the_yellows_and_ratings_stay_within_bounds()
    {
        var sentOff = PerformanceRating.Calculate(Line(minutes: 20, yellows: 2, red: true), Position.Defender, 0, 6)!;
        var hatTrick = PerformanceRating.Calculate(Line(goals: 4, assists: 3, shots: 8, onTarget: 8, keyPasses: 6), Position.Forward, 7, 0)!;

        Assert.Equal(-1.5, sentOff.Discipline);
        Assert.True(sentOff.Total >= PerformanceRating.Min);
        Assert.Equal(PerformanceRating.Max, hatTrick.Total);
    }

    [Fact]
    public void Unknown_position_is_rated_as_a_midfielder()
    {
        var unknown = PerformanceRating.Calculate(Line(goals: 1, tackles: 3), null, 1, 0);
        var midfielder = PerformanceRating.Calculate(Line(goals: 1, tackles: 3), Position.Midfielder, 1, 0);

        Assert.Equal(midfielder, unknown);
    }

    [Fact]
    public void Changing_the_position_recalculates_the_stored_rating()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var match = TestData.PlayedMatch(2, 0);
        var line = PlayerMatchPerformance.Record(squad, entry, match, coachId, TestData.ValidStats(), TestData.Now);
        var asForward = line.Rating;

        squad.UpdatePlayer(entry.Id, entry.FullName, Position.Defender, entry.ShirtNumber);
        line.Recalculate(entry, match);

        Assert.NotEqual(asForward, line.Rating);
        Assert.Throws<DomainException>(() => line.Recalculate(squad.AddPlayer("Otro", null, null, null, TestData.Now), match));
    }
}
