using TercerTiempo.Application.Stats.Predictions;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// Previsor de resultados: probabilidades de campeón, ascenso o promoción y descenso con lo que se sabe en cada jornada.
public class LeagueForecastTests
{
    private static readonly ForecastZones Zones = new(2, 5);

    // Liga de seis a doble vuelta: en la primera vuelta gana siempre el de número más bajo («0» lo gana todo y «5» lo pierde
    // todo), unas veces en casa y otras fuera; la segunda vuelta, con los campos cambiados, está por jugar.
    private static (List<Guid> Teams, List<ForecastMatch> Matches) League()
    {
        var teams = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var matches = new List<ForecastMatch>();
        for (var i = 0; i < 6; i++)
            for (var j = i + 1; j < 6; j++)
            {
                var winnerAtHome = (i + j) % 2 == 0;
                var (home, away) = winnerAtHome ? (teams[i], teams[j]) : (teams[j], teams[i]);
                matches.Add(winnerAtHome ? new ForecastMatch(home, away, 3, 1) : new ForecastMatch(home, away, 1, 3));
                matches.Add(new ForecastMatch(away, home, null, null));
            }
        return (teams, matches);
    }

    [Fact]
    public void Probabilities_add_up_and_the_same_data_gives_the_same_forecast()
    {
        var (teams, matches) = League();
        var forecast = LeagueForecaster.Run(teams, matches, Zones, 4000, 42);

        Assert.Equal(1, forecast.Sum(t => t.Champion), 3);
        Assert.Equal(2, forecast.Sum(t => t.Promotion), 3);
        Assert.Equal(2, forecast.Sum(t => t.Relegation), 3);
        Assert.All(forecast, t => Assert.Equal(1, t.PositionOdds.Sum(), 3));
        for (var p = 0; p < teams.Count; p++) Assert.Equal(1, forecast.Sum(t => t.PositionOdds[p]), 3);

        var again = LeagueForecaster.Run(teams, matches, Zones, 4000, 42);
        Assert.Equal(forecast.Select(t => (t.TeamId, t.Champion, t.ExpectedPoints)), again.Select(t => (t.TeamId, t.Champion, t.ExpectedPoints)));
    }

    [Fact]
    public void The_leader_is_favourite_and_the_bottom_team_is_likely_to_go_down()
    {
        var (teams, matches) = League();
        var forecast = LeagueForecaster.Run(teams, matches, Zones, 4000, 7).ToDictionary(t => t.TeamId);

        var leader = forecast[teams[0]];
        var bottom = forecast[teams[5]];
        Assert.Equal((1, 15, 5), (leader.Position, leader.Points, leader.Played));
        Assert.Equal(leader.Champion, forecast.Values.Max(t => t.Champion));
        Assert.True(leader.Champion > 0.4 && leader.Promotion > 0.8, $"Campeón {leader.Champion}, promoción {leader.Promotion}");
        Assert.True(leader.Attack > 1 && leader.Defense < 1, $"Ataque {leader.Attack}, defensa {leader.Defense}");
        Assert.Equal(bottom.Relegation, forecast.Values.Max(t => t.Relegation));
        Assert.True(bottom.Relegation > 0.5, $"Descenso {bottom.Relegation}");
        Assert.True(bottom.Champion < 0.01);
        Assert.Equal(teams[0], LeagueForecaster.Run(teams, matches, Zones, 4000, 7)[0].TeamId);
    }

    [Fact]
    public void When_everything_is_played_the_forecast_is_the_final_table()
    {
        var teams = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var matches = new List<ForecastMatch>
        {
            new(teams[0], teams[1], 2, 0), new(teams[1], teams[0], 1, 1),
            new(teams[0], teams[2], 3, 1), new(teams[2], teams[0], 0, 2),
            new(teams[1], teams[2], 1, 0), new(teams[2], teams[1], 2, 2),
        };
        var forecast = LeagueForecaster.Run(teams, matches, new ForecastZones(1, 3), 500, 1);

        Assert.Equal([teams[0], teams[1], teams[2]], forecast.Select(t => t.TeamId));
        Assert.Equal([1.0, 0.0, 0.0], forecast.Select(t => t.Champion));
        Assert.Equal([0.0, 0.0, 1.0], forecast.Select(t => t.Relegation));
        Assert.Equal([10.0, 5.0, 1.0], forecast.Select(t => t.ExpectedPoints));
    }

    [Fact]
    public void Only_results_up_to_the_chosen_matchday_count()
    {
        var competition = Competition.Create("1ª RFEF", "1ª RFEF", CompetitionLevel.National, 1, null, "España", null);
        var season = competition.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null);
        var (home, away) = (Team.Create("CD Lugo", "LUG", null), Team.Create("Arenas Club", "ARE", null));
        var first = Match.Schedule(season.Id, 1, home.Id, away.Id, DateTimeOffset.UtcNow.AddDays(-14), null, null);
        first.RecordResult(2, 0, DateTimeOffset.UtcNow);
        var second = Match.Schedule(season.Id, 2, away.Id, home.Id, DateTimeOffset.UtcNow.AddDays(-7), null, null);
        second.RecordResult(1, 1, DateTimeOffset.UtcNow);
        var cancelled = Match.Schedule(season.Id, 3, home.Id, away.Id, DateTimeOffset.UtcNow.AddDays(7), null, null);
        cancelled.Cancel();

        var source = new ForecastSource(
            competition, season, [first, second, cancelled], [home.Id, away.Id], new Dictionary<Guid, Team>(), [1, 2],
            LeagueZoneRules.For(competition, 2), "huella");

        var asOfFirst = source.AsOf(1);
        Assert.Equal(2, asOfFirst.Count);
        Assert.Equal([(2, 0), (null, null)], asOfFirst.Select(m => (m.HomeGoals, m.AwayGoals)).Select(x => ((int?, int?))x));
        Assert.All(source.AsOf(2), m => Assert.NotNull(m.HomeGoals));
    }

    [Theory]
    [InlineData(CompetitionLevel.National, 1, 20, 5, 16)]
    [InlineData(CompetitionLevel.National, 3, 18, 5, 15)]
    [InlineData(CompetitionLevel.Regional, 1, 20, 2, 17)]
    [InlineData(CompetitionLevel.Regional, 2, 18, 2, 16)]
    [InlineData(CompetitionLevel.Regional, 3, 16, 2, null)]
    public void Each_category_has_its_usual_zones(CompetitionLevel level, int tier, int teams, int promotionUntil, int? relegationFrom)
    {
        var competition = Competition.Create("Liga", "Liga", level, tier, null, "Asturias", null);
        Assert.Equal(new ForecastZones(promotionUntil, relegationFrom), LeagueZoneRules.For(competition, teams));
    }
}
