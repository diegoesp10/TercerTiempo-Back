using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Application.Competitions.Matchdays;

internal static class MatchdayCalculator
{
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    public static int? Current(IEnumerable<Match> matches, DateTimeOffset now)
    {
        var summaries = Summaries(matches);
        if (summaries.Count == 0) return null;

        foreach (var summary in summaries)
        {
            if (summary.ReferenceAt >= now - Grace)
                return summary.Number;
        }

        return summaries[^1].Number;
    }

    public static IReadOnlyList<MatchdaySummaryDto> Summaries(IEnumerable<Match> matches) =>
        matches
            .GroupBy(m => m.Matchday)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var kickoffs = g.Select(m => m.KickoffAt).Order().ToList();
                return new MatchdaySummaryDto(
                    g.Key,
                    kickoffs[0],
                    kickoffs[^1],
                    kickoffs[kickoffs.Count / 2],
                    g.Count(),
                    g.Count(m => m.Status == MatchStatus.Played));
            })
            .ToList();
}
