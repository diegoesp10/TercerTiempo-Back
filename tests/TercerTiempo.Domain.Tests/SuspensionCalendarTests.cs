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
        Assert.DoesNotContain(calendar.For(season[5].Id), s => s.SquadPlayerId == Nel);
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
    public void The_cycle_says_which_yellows_count_and_why()
    {
        var season = Season(10);
        // Pelayo: amarillas en la J1 y la J2, doble amarilla en la J3 (cumple en la J4) y amarillas en la J5, J6, J7 y J9.
        // Nel: solo una doble amarilla.
        var calendar = SuspensionCalendar.Build(season, Cards(
            (season[0], Pelayo, 1, false), (season[1], Pelayo, 1, false), (season[2], Pelayo, 2, true),
            (season[4], Pelayo, 1, false), (season[5], Pelayo, 1, false), (season[6], Pelayo, 1, false),
            (season[8], Pelayo, 1, false), (season[1], Nel, 2, true)));

        // Llega apercibido a la J7 con las de la J1, J2, J5 y J6: las dos de la doble amarilla no cuentan.
        var warned = calendar.CycleBefore(Pelayo, season[6].Id);
        Assert.Equal([1, 2, 5, 6], warned.CycleMatchdays);
        Assert.Equal([3], warned.SecondYellowMatchdays);
        Assert.Empty(warned.CompletedAt);
        Assert.Equal(4, calendar.YellowsBefore(Pelayo, season[6].Id));
        Assert.Equal(SuspensionCause.SecondYellow, calendar.On(Pelayo, season[3].Id)!.Cause);

        // La de la J7 es la 5.ª: cumple ciclo, no juega la J8 y a la J10 llega con la de la J9.
        Assert.Equal((SuspensionCause.YellowCards, 5), (calendar.On(Pelayo, season[7].Id)!.Cause, calendar.On(Pelayo, season[7].Id)!.Yellows));
        var last = calendar.CycleBefore(Pelayo, season[9].Id);
        Assert.Equal([9], last.CycleMatchdays);
        Assert.Equal([7], last.CompletedAt);
        Assert.Equal([3], last.SecondYellowMatchdays);
        Assert.Equal(1, calendar.YellowsBefore(Pelayo, season[9].Id));

        // Con solo una doble amarilla no lleva ninguna en el ciclo, pero sí el porqué; quien no ha visto ninguna, nada.
        var nel = calendar.CycleBefore(Nel, season[9].Id);
        Assert.Equal((0, false), (nel.InCycle, nel.IsEmpty));
        Assert.Equal([2], nel.SecondYellowMatchdays);
        Assert.True(calendar.CycleBefore(Guid.NewGuid(), season[9].Id).IsEmpty);
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

    [Fact]
    public void What_the_committee_publishes_replaces_the_minimum_of_the_cards()
    {
        var season = Season(6);
        var calendar = SuspensionCalendar.Build(season, Cards((season[0], Pelayo, 0, true)), Decisions(
            (season[0], Pelayo, new CommitteePenalty(3, "Agredir a otro", new DateOnly(2026, 9, 15)))));

        var served = season.Select(m => calendar.On(Pelayo, m.Id)).ToList();
        Assert.Equal([null, 1, 2, 3, null, null], served.Select(s => s?.Nth));
        Assert.All(served.Where(s => s is not null), s => Assert.Equal((SuspensionCause.Committee, 3, "Agredir a otro", 1), (s!.Cause, s.Matches, s.Detail, s.FromMatchday)));
        Assert.Equal(new DateOnly(2026, 9, 15), served[1]!.DecidedOn);
    }

    [Fact]
    public void A_player_the_committee_does_not_name_keeps_the_minimum_of_his_cards()
    {
        var season = Season(8);
        var calendar = SuspensionCalendar.Build(season, Cards(
                (season[0], Pelayo, 1, false), (season[1], Pelayo, 1, false), (season[2], Pelayo, 1, false), (season[3], Pelayo, 1, false),
                (season[4], Pelayo, 1, true), (season[4], Nel, 0, true)),
            Decisions((season[4], Nel, new CommitteePenalty(2, "Producirse de manera violenta", new DateOnly(2026, 10, 20)))));

        // Nel: los dos partidos del comité en vez del mínimo de la roja.
        Assert.Equal([1, 2], season.Skip(5).Take(2).Select(m => calendar.On(Nel, m.Id)!.Nth));
        Assert.Null(calendar.On(Nel, season[7].Id));
        // Pelayo no sale en lo publicado: nunca se le quita una sanción por no encontrarla. La roja y la quinta amarilla,
        // una detrás de otra, y el ciclo vuelve a cero.
        Assert.Equal([SuspensionCause.RedCard, SuspensionCause.YellowCards], season.Skip(5).Take(2).Select(m => calendar.On(Pelayo, m.Id)!.Cause));
        Assert.Equal(0, calendar.YellowsBefore(Pelayo, season[5].Id));
        Assert.Equal(season[4].Id, Assert.Single(calendar.Decisions).Key);
    }

    [Fact]
    public void Two_sanctions_for_the_same_match_are_served_one_after_the_other()
    {
        var season = Season(6);
        var calendar = SuspensionCalendar.Build(season, Cards((season[0], Pelayo, 0, true)), Decisions(
            (season[0], Pelayo, new CommitteePenalty(1, "Provocar a otro", new DateOnly(2026, 9, 22))),
            (season[0], Pelayo, new CommitteePenalty(2, "Menosprecio al árbitro", new DateOnly(2026, 9, 22)))));

        Assert.Equal(["Provocar a otro", "Menosprecio al árbitro", "Menosprecio al árbitro"],
            season.Skip(1).Take(3).Select(m => calendar.On(Pelayo, m.Id)!.Detail));
        Assert.Null(calendar.On(Pelayo, season[4].Id));
    }

    private static Dictionary<Guid, CommitteeDecision> Decisions(params (Match Match, Guid Player, CommitteePenalty Penalty)[] penalties) =>
        penalties.GroupBy(p => p.Match.Id).ToDictionary(
            g => g.Key,
            g => new CommitteeDecision(g.GroupBy(p => p.Player).ToDictionary(p => p.Key, p => (IReadOnlyList<CommitteePenalty>)p.Select(x => x.Penalty).ToList())));

    private static List<Match> Season(int matches) =>
        Enumerable.Range(1, matches)
            .Select(matchday => Match.Schedule(TestData.SeasonId, matchday, TestData.HomeTeamId, TestData.AwayTeamId, TestData.Now.AddDays(7 * matchday), null, null))
            .ToList();

    private static Dictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>> Cards(params (Match Match, Guid Player, int Yellows, bool Red)[] lines) =>
        lines.GroupBy(l => l.Match.Id).ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<Guid, MatchCards>)g.ToDictionary(l => l.Player, l => new MatchCards(l.Yellows, l.Red)));
}
