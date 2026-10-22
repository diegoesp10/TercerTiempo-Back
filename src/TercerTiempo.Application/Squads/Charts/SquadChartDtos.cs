using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Application.Squads.Charts;

public sealed record SquadPlayerProfileDto(
    SquadSummaryDto Squad,
    SquadPlayerDto Player,
    IndividualStatsDto Stats,
    TeamImpactDto Impact,
    PlayerAxesDto Profile,
    PlayerAxesDto SquadAverage,
    PlayerAxesDto SquadBest,
    IReadOnlyList<PlayerMatchPointDto> Matches);

public sealed record PlayerMatchPointDto(
    MatchDto Match,
    bool Home,
    string OpponentName,
    string OpponentShortName,
    int? GoalsFor,
    int? GoalsAgainst,
    MatchOutcome Outcome,
    PerformanceDto? Performance,
    RatingBreakdownDto? Rating,
    [property: System.ComponentModel.Description("Su línea en el acta oficial de la federación, si está descargada.")]
    OfficialSheets.OfficialLineDto? Official = null);

public sealed record RatingBreakdownDto(
    double Attack,
    double Creation,
    double Defense,
    double Goalkeeping,
    double Discipline,
    double Result,
    double Total);

public sealed record SquadTimelineDto(SquadSummaryDto Squad, IReadOnlyList<SquadTimelinePointDto> Matches);

public sealed record SquadTimelinePointDto(
    MatchDto Match,
    bool Home,
    string OpponentName,
    string OpponentShortName,
    int GoalsFor,
    int GoalsAgainst,
    MatchOutcome Outcome,
    int Points,
    int CumulativePoints,
    bool SheetRecorded,
    int PlayersUsed,
    double? AverageRating,
    string? BestPlayerName,
    double? BestRating);
