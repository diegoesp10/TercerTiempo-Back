using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Application.Stats;

/// <summary>Funciones puras de agregación. Sin dependencias: fáciles de testear.</summary>
internal static class PlayerStatsCalculator
{
    public static IndividualStatsDto Individual(IReadOnlyCollection<PlayerMatchPerformance> performances)
    {
        if (performances.Count == 0) return IndividualStatsDto.Empty;

        var minutes = performances.Sum(p => p.MinutesPlayed);
        var goals = performances.Sum(p => p.Goals);
        var assists = performances.Sum(p => p.Assists);
        var shots = performances.Sum(p => p.Shots);
        var shotsOnTarget = performances.Sum(p => p.ShotsOnTarget);

        return new IndividualStatsDto(
            MatchesLogged: performances.Count,
            Starts: performances.Count(p => p.Started),
            Minutes: minutes,
            Goals: goals,
            Assists: assists,
            GoalContributions: goals + assists,
            Shots: shots,
            ShotsOnTarget: shotsOnTarget,
            KeyPasses: performances.Sum(p => p.KeyPasses),
            Tackles: performances.Sum(p => p.Tackles),
            Interceptions: performances.Sum(p => p.Interceptions),
            Saves: performances.Sum(p => p.Saves),
            YellowCards: performances.Sum(p => p.YellowCards),
            RedCards: performances.Count(p => p.RedCard),
            AverageRating: Round(performances.Average(p => (double)p.SelfRating)),
            GoalsPer90: Per90(goals, minutes),
            AssistsPer90: Per90(assists, minutes),
            GoalContributionsPer90: Per90(goals + assists, minutes),
            ShotAccuracyPct: shots > 0 ? Round(shotsOnTarget * 100.0 / shots) : null);
    }

    public static TeamImpactDto TeamImpact(
        Guid teamId,
        IReadOnlyCollection<Match> seasonMatches,
        IReadOnlyCollection<PlayerMatchPerformance> playerPerformances)
    {
        var played = seasonMatches
            .Where(m => m.Involves(teamId) && m.Status == MatchStatus.Played)
            .ToList();

        var byMatch = playerPerformances
            .Where(p => p.TeamId == teamId)
            .ToDictionary(p => p.MatchId);

        var withPlayer = played.Where(m => byMatch.TryGetValue(m.Id, out var p) && p.MinutesPlayed > 0).ToList();
        var withoutPlayer = played.Except(withPlayer).ToList();

        var teamGoalsFor = played.Sum(m => m.GoalsFor(teamId) ?? 0);
        var teamGoalsAgainst = played.Sum(m => m.GoalsAgainst(teamId) ?? 0);
        var teamPoints = played.Sum(m => m.PointsFor(teamId) ?? 0);

        var playerGoals = byMatch.Values.Sum(p => p.Goals);
        var playerContributions = byMatch.Values.Sum(p => p.GoalContributions);
        var playerMinutes = byMatch.Values.Sum(p => p.MinutesPlayed);

        return new TeamImpactDto(
            TeamId: teamId,
            TeamMatchesPlayed: played.Count,
            TeamGoalsFor: teamGoalsFor,
            TeamGoalsAgainst: teamGoalsAgainst,
            TeamPoints: teamPoints,
            ParticipationPct: played.Count > 0 ? Round(Math.Min(100, playerMinutes * 100.0 / (played.Count * 90))) : 0,
            GoalSharePct: teamGoalsFor > 0 ? Round(playerGoals * 100.0 / teamGoalsFor) : null,
            GoalContributionSharePct: teamGoalsFor > 0 ? Round(Math.Min(100, playerContributions * 100.0 / teamGoalsFor)) : null,
            MatchesWithPlayer: withPlayer.Count,
            WinsWithPlayer: withPlayer.Count(m => m.OutcomeFor(teamId) == MatchOutcome.Win),
            DrawsWithPlayer: withPlayer.Count(m => m.OutcomeFor(teamId) == MatchOutcome.Draw),
            LossesWithPlayer: withPlayer.Count(m => m.OutcomeFor(teamId) == MatchOutcome.Loss),
            PointsPerMatchWithPlayer: Average(withPlayer, m => m.PointsFor(teamId) ?? 0),
            GoalsForPerMatchWithPlayer: Average(withPlayer, m => m.GoalsFor(teamId) ?? 0),
            GoalsAgainstPerMatchWithPlayer: Average(withPlayer, m => m.GoalsAgainst(teamId) ?? 0),
            MatchesWithoutPlayer: withoutPlayer.Count,
            PointsPerMatchWithoutPlayer: Average(withoutPlayer, m => m.PointsFor(teamId) ?? 0),
            GoalsForPerMatchWithoutPlayer: Average(withoutPlayer, m => m.GoalsFor(teamId) ?? 0),
            GoalsAgainstPerMatchWithoutPlayer: Average(withoutPlayer, m => m.GoalsAgainst(teamId) ?? 0));
    }

    public static LeagueComparisonDto? LeagueComparison(
        Guid playerId,
        Guid? teamId,
        IReadOnlyCollection<Standing> standings,
        IReadOnlyCollection<PlayerMatchPerformance> seasonPerformances)
    {
        if (standings.Count == 0) return null;

        var totalPlayed = standings.Sum(s => s.Played);
        var leagueAvgGoals = totalPlayed > 0 ? standings.Sum(s => s.GoalsFor) / (double)totalPlayed : 0;
        var leagueAvgPoints = totalPlayed > 0 ? standings.Sum(s => s.Points) / (double)totalPlayed : 0;

        var teamStanding = teamId is { } id ? standings.FirstOrDefault(s => s.TeamId == id) : null;

        var lines = seasonPerformances
            .GroupBy(p => p.PlayerId)
            .Select(g => new PlayerSeasonLine(
                g.Key,
                g.Sum(p => p.MinutesPlayed),
                g.Sum(p => p.Goals),
                g.Sum(p => p.Assists),
                g.Average(p => (double)p.SelfRating)))
            .Where(l => l.Minutes > 0)
            .ToList();

        var me = lines.FirstOrDefault(l => l.PlayerId == playerId);

        return new LeagueComparisonDto(
            TeamsCount: standings.Count,
            TeamPosition: teamStanding?.Position,
            LeagueAvgGoalsForPerMatch: Round(leagueAvgGoals),
            LeagueAvgPointsPerMatch: Round(leagueAvgPoints),
            TeamGoalsForPerMatch: teamStanding is { Played: > 0 } ? Round(teamStanding.GoalsFor / (double)teamStanding.Played) : null,
            TeamGoalsAgainstPerMatch: teamStanding is { Played: > 0 } ? Round(teamStanding.GoalsAgainst / (double)teamStanding.Played) : null,
            TeamPointsPerMatch: teamStanding is { Played: > 0 } ? Round(teamStanding.Points / (double)teamStanding.Played) : null,
            PlayersOnPlatform: lines.Count,
            GoalsPer90Percentile: Percentile(lines, me, l => l.Goals * 90.0 / l.Minutes),
            AssistsPer90Percentile: Percentile(lines, me, l => l.Assists * 90.0 / l.Minutes),
            RatingPercentile: Percentile(lines, me, l => l.AverageRating),
            MinutesPercentile: Percentile(lines, me, l => l.Minutes));
    }

    /// <summary>Porcentaje de jugadores con un valor igual o inferior al del jugador (rango percentil).</summary>
    internal static double? Percentile(
        IReadOnlyCollection<PlayerSeasonLine> lines, PlayerSeasonLine? me, Func<PlayerSeasonLine, double> selector)
    {
        if (me is null || lines.Count < 2) return null;

        var mine = selector(me);
        var below = lines.Count(l => selector(l) < mine);
        var equal = lines.Count(l => Math.Abs(selector(l) - mine) < 0.0001);

        return Round((below + 0.5 * equal) * 100.0 / lines.Count);
    }

    internal sealed record PlayerSeasonLine(Guid PlayerId, int Minutes, int Goals, int Assists, double AverageRating);

    private static double? Per90(int value, int minutes) => minutes > 0 ? Round(value * 90.0 / minutes) : null;

    private static double? Average(IReadOnlyCollection<Match> matches, Func<Match, int> selector) =>
        matches.Count > 0 ? Round(matches.Average(selector)) : null;

    private static double Round(double value) => Math.Round(value, 2);
}
