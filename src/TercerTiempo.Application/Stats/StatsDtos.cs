using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Players;

namespace TercerTiempo.Application.Stats;

public sealed record PlayerDashboardDto(
    PlayerDto Player,
    SeasonDto? Season,
    IndividualStatsDto Individual,
    TeamImpactDto? TeamImpact,
    LeagueComparisonDto? League,
    IReadOnlyList<PerformanceWithMatchDto> RecentPerformances);

public sealed record IndividualStatsDto(
    int MatchesLogged,
    int Starts,
    int Minutes,
    int Goals,
    int Assists,
    int GoalContributions,
    int Shots,
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    int YellowCards,
    int RedCards,
    double? AverageRating,
    double? GoalsPer90,
    double? AssistsPer90,
    double? GoalContributionsPer90,
    double? ShotAccuracyPct)
{
    public static readonly IndividualStatsDto Empty =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null);
}

public sealed record TeamImpactDto(
    Guid TeamId,
    int TeamMatchesPlayed,
    int TeamGoalsFor,
    int TeamGoalsAgainst,
    int TeamPoints,
    double ParticipationPct,
    double? GoalSharePct,
    double? GoalContributionSharePct,
    int MatchesWithPlayer,
    int WinsWithPlayer,
    int DrawsWithPlayer,
    int LossesWithPlayer,
    double? PointsPerMatchWithPlayer,
    double? GoalsForPerMatchWithPlayer,
    double? GoalsAgainstPerMatchWithPlayer,
    int MatchesWithoutPlayer,
    double? PointsPerMatchWithoutPlayer,
    double? GoalsForPerMatchWithoutPlayer,
    double? GoalsAgainstPerMatchWithoutPlayer);

public sealed record LeagueComparisonDto(
    int TeamsCount,
    int? TeamPosition,
    double LeagueAvgGoalsForPerMatch,
    double LeagueAvgPointsPerMatch,
    double? TeamGoalsForPerMatch,
    double? TeamGoalsAgainstPerMatch,
    double? TeamPointsPerMatch,
    int PlayersCompared,
    double? GoalsPer90Percentile,
    double? AssistsPer90Percentile,
    double? RatingPercentile,
    double? MinutesPercentile);

public sealed record LeagueTableDto(
    [property: System.ComponentModel.Description("null si la competición aún no tiene temporada en curso.")]
    SeasonDto? Season,
    IReadOnlyList<Standings.StandingDto> Standings,
    [property: System.ComponentModel.Description("Explicación cuando no hay clasificación (temporada nueva o sin partidos).")]
    string? Notice);
