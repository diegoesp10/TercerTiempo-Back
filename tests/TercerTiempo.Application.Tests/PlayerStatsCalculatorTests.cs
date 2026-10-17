using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Application.Tests;

public class PlayerStatsCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SeasonId = Guid.NewGuid();
    private static readonly Guid MyTeam = Guid.NewGuid();
    private static readonly Guid Rival = Guid.NewGuid();

    [Fact]
    public void Individual_stats_aggregate_and_compute_per_90()
    {
        var (player, performances, _) = Scenario();

        var stats = PlayerStatsCalculator.Individual(performances);

        Assert.Equal(3, stats.MatchesLogged);
        Assert.Equal(2, stats.Starts);
        Assert.Equal(225, stats.Minutes);
        Assert.Equal(3, stats.Goals);
        Assert.Equal(1, stats.Assists);
        Assert.Equal(4, stats.GoalContributions);
        Assert.Equal(1.2, stats.GoalsPer90);
        Assert.Equal(7.0, stats.AverageRating);
        Assert.Equal(player.Id, performances[0].PlayerId);
    }

    [Fact]
    public void Empty_performances_return_empty_stats()
    {
        var stats = PlayerStatsCalculator.Individual([]);

        Assert.Equal(IndividualStatsDto.Empty, stats);
        Assert.Null(stats.GoalsPer90);
    }

    [Fact]
    public void Team_impact_separates_matches_with_and_without_player()
    {
        var (_, performances, matches) = Scenario();

        var impact = PlayerStatsCalculator.TeamImpact(MyTeam, matches, performances);

        Assert.Equal(4, impact.TeamMatchesPlayed);
        Assert.Equal(3, impact.MatchesWithPlayer);
        Assert.Equal(1, impact.MatchesWithoutPlayer);
        Assert.Equal(2, impact.WinsWithPlayer);
        Assert.Equal(1, impact.DrawsWithPlayer);
        Assert.Equal(0, impact.LossesWithPlayer);
        Assert.Equal(2.33, impact.PointsPerMatchWithPlayer);
        Assert.Equal(0, impact.PointsPerMatchWithoutPlayer);
        Assert.Equal(7, impact.TeamGoalsFor);
        Assert.Equal(42.86, impact.GoalSharePct);
    }

    [Fact]
    public void League_comparison_computes_percentiles_among_platform_players()
    {
        var (player, performances, matches) = Scenario();
        var played = matches.Where(m => m.Status == MatchStatus.Played).ToList();

        var other = Player.Register(Guid.NewGuid(), "Rival Goleador", Position.Forward, null, Now);
        other.JoinSquad(Rival, SeasonId, 10, true, true, Now);
        var otherPerformances = played.Take(2)
            .Select(m => PlayerMatchPerformance.Log(other, m, Rival,
                new PerformanceStats(true, 90, 0, 0, 1, 0, 0, 2, 2, 0, 0, false, 5, null), Now))
            .ToList();

        var standings = new List<Standing>
        {
            Standing.Create(SeasonId, MyTeam, new StandingLine(1, 4, 2, 1, 1, 7, 4, 7), Now),
            Standing.Create(SeasonId, Rival, new StandingLine(2, 4, 1, 1, 2, 4, 7, 4), Now),
        };

        var league = PlayerStatsCalculator.LeagueComparison(player.Id, MyTeam, standings, [.. performances, .. otherPerformances]);

        Assert.NotNull(league);
        Assert.Equal(2, league!.TeamsCount);
        Assert.Equal(1, league.TeamPosition);
        Assert.Equal(2, league.PlayersOnPlatform);
        Assert.Equal(1.38, league.LeagueAvgGoalsForPerMatch);
        Assert.Equal(75, league.GoalsPer90Percentile);
        Assert.Equal(75, league.RatingPercentile);
        Assert.Equal(1.75, league.TeamGoalsForPerMatch);
    }

    [Fact]
    public void League_comparison_is_null_without_standings()
    {
        Assert.Null(PlayerStatsCalculator.LeagueComparison(Guid.NewGuid(), null, [], []));
    }

    /// <summary>
    /// Mi equipo juega 4 partidos: gana 2 y empata 1 conmigo (90, 90 y 45 minutos), y pierde 1 sin mí.
    /// Goles del equipo: 3 + 2 + 2 + 0 = 7. Yo marco 2 + 1 + 0 = 3 y doy 1 asistencia.
    /// </summary>
    private static (Player Player, List<PlayerMatchPerformance> Performances, List<Match> Matches) Scenario()
    {
        var player = Player.Register(Guid.NewGuid(), "Pelayo Fernández", Position.Forward, null, Now);
        player.JoinSquad(MyTeam, SeasonId, 9, true, true, Now);

        var matches = new List<Match>
        {
            Played(1, MyTeam, Rival, 3, 1),
            Played(2, Rival, MyTeam, 0, 2),
            Played(3, MyTeam, Rival, 2, 2),
            Played(4, Rival, MyTeam, 1, 0),
            Match.Schedule(SeasonId, 5, MyTeam, Rival, Now.AddDays(7), null, null),
        };

        var performances = new List<PlayerMatchPerformance>
        {
            PlayerMatchPerformance.Log(player, matches[0], MyTeam, Stats(90, 2, 0, 8, started: true), Now),
            PlayerMatchPerformance.Log(player, matches[1], MyTeam, Stats(90, 1, 1, 7, started: true), Now),
            PlayerMatchPerformance.Log(player, matches[2], MyTeam, Stats(45, 0, 0, 6, started: false), Now),
        };

        return (player, performances, matches);
    }

    private static Match Played(int matchday, Guid home, Guid away, int homeGoals, int awayGoals)
    {
        var match = Match.Schedule(SeasonId, matchday, home, away, Now.AddDays(-7 * (6 - matchday)), null, null);
        match.RecordResult(homeGoals, awayGoals, Now);
        return match;
    }

    private static PerformanceStats Stats(int minutes, int goals, int assists, int rating, bool started) =>
        new(started, minutes, goals, assists, goals + 2, goals + 1, 1, 1, 0, 0, 0, false, rating, null);
}
