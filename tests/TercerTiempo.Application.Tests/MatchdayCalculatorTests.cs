using TercerTiempo.Application.Competitions.Matchdays;
using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Application.Tests;

public class MatchdayCalculatorTests
{
    private static readonly Guid SeasonId = Guid.NewGuid();
    private static readonly Guid[] Teams = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    // Jornada 5: 10-11 de octubre (jugada). Jornada 6: 17-18 de octubre. Jornada 7: 25 de octubre.
    private static List<Match> Season() =>
    [
        Played(5, new DateTimeOffset(2026, 10, 10, 16, 0, 0, TimeSpan.Zero)),
        Played(5, new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero)),
        Scheduled(6, new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero)),
        Scheduled(6, new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero)),
        Scheduled(7, new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero)),
        Scheduled(7, new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero)),
    ];

    [Theory]
    [InlineData("2026-10-13T10:00:00Z", 6)] // martes: la próxima es la 6
    [InlineData("2026-10-17T09:00:00Z", 6)] // sábado de la jornada 6
    [InlineData("2026-10-18T20:00:00Z", 6)] // domingo por la noche: sigue siendo la 6
    [InlineData("2026-10-19T12:00:00Z", 7)] // lunes: pasa a la 7
    [InlineData("2026-12-01T00:00:00Z", 7)] // temporada acabada: la última
    public void Current_matchday_is_the_one_in_progress_or_the_next(string now, int expected)
    {
        Assert.Equal(expected, MatchdayCalculator.Current(Season(), DateTimeOffset.Parse(now)));
    }

    [Fact]
    public void A_postponed_match_does_not_keep_an_old_matchday_current()
    {
        var matches = Season();
        // Un partido de la jornada 5 aplazado a noviembre.
        matches.Add(Scheduled(5, new DateTimeOffset(2026, 11, 11, 19, 0, 0, TimeSpan.Zero)));

        Assert.Equal(6, MatchdayCalculator.Current(matches, new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Summaries_describe_each_matchday()
    {
        var summaries = MatchdayCalculator.Summaries(Season());

        Assert.Equal([5, 6, 7], summaries.Select(s => s.Number));
        var j6 = summaries[1];
        Assert.Equal(2, j6.Matches);
        Assert.Equal(0, j6.Played);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero), j6.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero), j6.EndsAt);
        Assert.Equal(2, summaries[0].Played);
    }

    [Fact]
    public void No_matches_means_no_current_matchday()
    {
        Assert.Null(MatchdayCalculator.Current([], DateTimeOffset.UtcNow));
    }

    private static Match Scheduled(int matchday, DateTimeOffset kickoff) =>
        Match.Schedule(SeasonId, matchday, Teams[0], Teams[1], kickoff, null, null);

    private static Match Played(int matchday, DateTimeOffset kickoff)
    {
        var match = Scheduled(matchday, kickoff);
        match.RecordResult(1, 0, kickoff.AddHours(2));
        return match;
    }
}
