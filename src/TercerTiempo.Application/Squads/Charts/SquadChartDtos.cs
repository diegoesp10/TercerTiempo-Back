using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Performances;
using TercerTiempo.Application.Stats;
using TercerTiempo.Domain.Matches;

namespace TercerTiempo.Application.Squads.Charts;

/// <summary>Ficha de un jugador de la plantilla con todo lo necesario para sus gráficos.</summary>
/// <param name="Profile">Su perfil por 90'.</param>
/// <param name="SquadAverage">Media de la plantilla (jugadores con 90' o más).</param>
/// <param name="SquadBest">Máximo de la plantilla en cada eje, para escalar el radar.</param>
/// <param name="Matches">Todos los partidos disputados por el equipo, haya jugado él o no, en orden cronológico.</param>
public sealed record SquadPlayerProfileDto(
    SquadSummaryDto Squad,
    SquadPlayerDto Player,
    IndividualStatsDto Stats,
    TeamImpactDto Impact,
    PlayerAxesDto Profile,
    PlayerAxesDto SquadAverage,
    PlayerAxesDto SquadBest,
    IReadOnlyList<PlayerMatchPointDto> Matches);

/// <summary>Un partido del equipo desde el punto de vista de un jugador: su línea del acta (si jugó) y el desglose de su nota.</summary>
public sealed record PlayerMatchPointDto(
    MatchDto Match,
    bool Home,
    string OpponentName,
    string OpponentShortName,
    int? GoalsFor,
    int? GoalsAgainst,
    MatchOutcome Outcome,
    PerformanceDto? Performance,
    RatingBreakdownDto? Rating);

/// <summary>Aportación de cada bloque a la nota (sobre la base de 6,0) y nota final.</summary>
public sealed record RatingBreakdownDto(
    double Attack,
    double Creation,
    double Defense,
    double Goalkeeping,
    double Discipline,
    double Result,
    double Total);

/// <summary>Evolución del equipo partido a partido.</summary>
public sealed record SquadTimelineDto(SquadSummaryDto Squad, IReadOnlyList<SquadTimelinePointDto> Matches);

/// <param name="CumulativePoints">Puntos acumulados tras este partido.</param>
/// <param name="AverageRating">Nota media de los jugadores con nota en el acta (null si no hay acta).</param>
/// <param name="BestPlayerName">Mejor nota del partido.</param>
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
