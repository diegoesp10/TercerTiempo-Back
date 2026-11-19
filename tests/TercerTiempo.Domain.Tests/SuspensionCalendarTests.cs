using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

// Sanciones de liga: cada cinco amarillas y cada expulsión cuestan el siguiente partido del equipo.
public class SuspensionCalendarTests
{
    private static readonly Guid Pelayo = Guid.NewGuid();
    private static readonly Guid Nel = Guid.NewGuid();

    [Fact]
    public void The_fifth_yellow_costs_the_next_match_and_the_count_starts_again()
    {
        var season = Season(8);
        var calendar = SuspensionCalendar.Build(season, Cards(
            (season[0], Pelayo, 1, false), (season[1], Pelayo, 1, false), (season[2], Pelayo, 1, false),
            (season[3], Pelayo, 1, false), (season[4], Pelayo, 1, false)));

        Assert.Null(calendar.On(Pelayo, season[4].Id));
        var suspension = calendar.On(Pelayo, season[5].Id);
        Assert.Equal((SuspensionCause.YellowCards, 5, season[4].Id, 5), (suspension!.Cause, suspension.Yellows, suspension.FromMatchId, suspension.FromMatchday));
        Assert.Null(calendar.On(Pelayo, season[6].Id));
        // Apercibido antes de la quinta; después el ciclo vuelve a cero.
        Assert.Equal(4, calendar.YellowsBefore(Pelayo, season[4].Id));
        Assert.Equal(0, calendar.YellowsBefore(Pelayo, season[5].Id));
        Assert.Empty(calendar.For(season[5].Id).Where(s => s.SquadPlayerId == Nel));
    }

    [Fact]
    public void The_tenth_yellow_costs_another_match()
    {
        var season = Season(12);
        var lines = Enumerable.Range(0, 11).Where(i => i != 5).Select(i => (season[i], Pelayo, 1, false)).ToArray();

        var calendar = SuspensionCalendar.Build(season, Cards(lines));

        Assert.Equal([6, 12], season.Where(m => calendar.On(Pelayo, m.Id) is not null).Select(m => m.Matchday));
        Assert.Equal(10, calendar.On(Pelayo, season[11].Id)!.Yellows);
    }

    [Fact]
    public void A_double_yellow_costs_one_match_and_those_yellows_do_not_count_for_the_cycle()
    {
        var season = Season(8);
        var calendar = SuspensionCalendar.Build(season, Cards(
            (season[0], Pelayo, 1, false), (season[1], Pelayo, 1, false), (season[2], Pelayo, 1, false), (season[3], Pelayo, 1, false),
            (season[4], Pelayo, 2, true)));

        Assert.Equal(SuspensionCause.SecondYellow, calendar.On(Pelayo, season[5].Id)!.Cause);
        Assert.Null(calendar.On(Pelayo, season[6].Id));
        Assert.Equal(4, calendar.YellowsBefore(Pelayo, season[6].Id));
    }

    [Fact]
    public void A_straight_red_costs_the_next_match_and_a_yellow_before_it_still_counts()
    {
        var season = Season(4);
        var calendar = SuspensionCalendar.Build(season, Cards((season[0], Pelayo, 1, true)));

        Assert.Equal(SuspensionCause.RedCard, calendar.On(Pelayo, season[1].Id)!.Cause);
        Assert.Null(calendar.On(Pelayo, season[2].Id));
        Assert.Equal(1, calendar.YellowsBefore(Pelayo, season[2].Id));
    }

    [Fact]
    public void Two_suspensions_at_once_are_served_one_per_match()
    {
        var season = Season(8);
        var calendar = SuspensionCalendar.Build(season, Cards(
            (season[0], Pelayo, 1, false), (season[1], Pelayo, 1, false), (season[2], Pelayo, 1, false), (season[3], Pelayo, 1, false),
            (season[4], Pelayo, 1, true)));

        Assert.Equal(SuspensionCause.RedCard, calendar.On(Pelayo, season[5].Id)!.Cause);
        Assert.Equal(SuspensionCause.YellowCards, calendar.On(Pelayo, season[6].Id)!.Cause);
        Assert.Null(calendar.On(Pelayo, season[7].Id));
    }

    [Fact]
    public void Postponed_and_cancelled_matches_do_not_count_as_served()
    {
        var season = Season(5);
        season[1].Postpone();
        season[2].Cancel();

        var calendar = SuspensionCalendar.Build(season, Cards((season[0], Pelayo, 0, true)));

        Assert.Null(calendar.On(Pelayo, season[1].Id));
        Assert.Null(calendar.On(Pelayo, season[2].Id));
        Assert.NotNull(calendar.On(Pelayo, season[3].Id));
    }

    [Fact]
    public void Matches_are_served_in_kickoff_order_even_when_a_matchday_was_moved()
    {
        var season = Season(4);
        // La jornada 2 se jugó después que la 3.
        season[1].Reschedule(season[2].KickoffAt.AddDays(2));

        var calendar = SuspensionCalendar.Build(season, Cards((season[0], Pelayo, 0, true)));

        Assert.NotNull(calendar.On(Pelayo, season[2].Id));
        Assert.Null(calendar.On(Pelayo, season[1].Id));
    }

    private static List<Match> Season(int matches) =>
        Enumerable.Range(1, matches)
            .Select(matchday => Match.Schedule(TestData.SeasonId, matchday, TestData.HomeTeamId, TestData.AwayTeamId, TestData.Now.AddDays(7 * matchday), null, null))
            .ToList();

    private static Dictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>> Cards(params (Match Match, Guid Player, int Yellows, bool Red)[] lines) =>
        lines.GroupBy(l => l.Match.Id).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<Guid, MatchCards>)g.ToDictionary(l => l.Player, l => new MatchCards(l.Yellows, l.Red)));
}
