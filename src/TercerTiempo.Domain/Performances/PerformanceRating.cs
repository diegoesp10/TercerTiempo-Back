using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Performances;

/// <summary>
/// Nota de un jugador en un partido calculada solo con datos: lo que registra el acta y el resultado del partido.
/// Nadie la pone a mano. Escala 3,0-10,0 con un decimal; parte de <see cref="Base"/> y suma o resta por bloques:
///
/// <list type="bullet">
/// <item><b>Ataque</b>: goles (valen más cuanto más atrás juega el jugador) y tiros a puerta.</item>
/// <item><b>Creación</b>: asistencias y pases clave.</item>
/// <item><b>Defensa</b>: entradas e intercepciones; para defensas y medios, portería a cero (con 60' o más) y goles
/// encajados (proporcional a los minutos).</item>
/// <item><b>Portería</b>: paradas; para porteros, portería a cero y goles encajados.</item>
/// <item><b>Disciplina</b>: amarillas y roja.</item>
/// <item><b>Resultado</b>: victoria, empate o derrota, proporcional a los minutos jugados.</item>
/// </list>
///
/// Sin minutos no hay nota. Sin posición conocida se puntúa como centrocampista.
/// </summary>
public static class PerformanceRating
{
    public const double Base = 6.0;
    public const double Min = 3.0;
    public const double Max = 10.0;

    /// <summary>Minutos mínimos para que cuente la portería a cero.</summary>
    public const int CleanSheetMinutes = 60;

    private sealed record Weights(
        double Goal, double Assist, double KeyPass, double ShotOnTarget,
        double Tackle, double Interception, double Save,
        double CleanSheet, double GoalConceded);

    private static readonly Weights Goalkeeper = new(1.5, 0.8, 0.25, 0.10, 0.10, 0.10, 0.35, 1.0, -0.30);
    private static readonly Weights Defender = new(1.2, 0.8, 0.20, 0.10, 0.15, 0.15, 0.30, 0.6, -0.20);
    private static readonly Weights Midfielder = new(1.0, 0.7, 0.20, 0.12, 0.12, 0.12, 0.30, 0.2, -0.05);
    private static readonly Weights Forward = new(0.9, 0.7, 0.15, 0.15, 0.10, 0.08, 0.30, 0.0, 0.00);

    private const double YellowCard = -0.3;
    private const double RedCard = -1.5;
    private const double Win = 0.4;
    private const double Draw = 0.1;
    private const double Loss = -0.3;

    /// <param name="goalsFor">Goles del equipo del jugador en el partido.</param>
    /// <param name="goalsAgainst">Goles encajados por su equipo.</param>
    /// <returns>Desglose y nota final; null si el jugador no llegó a jugar (0 minutos).</returns>
    public static RatingBreakdown? Calculate(PerformanceStats stats, Position? position, int goalsFor, int goalsAgainst)
    {
        if (stats.MinutesPlayed <= 0) return null;

        var w = WeightsFor(position);
        var isGoalkeeper = position == Position.Goalkeeper;
        var share = Math.Min(stats.MinutesPlayed, 90) / 90.0;

        var attack = stats.Goals * w.Goal + stats.ShotsOnTarget * w.ShotOnTarget;
        var creation = stats.Assists * w.Assist + stats.KeyPasses * w.KeyPass;

        var resistance = (goalsAgainst == 0 && stats.MinutesPlayed >= CleanSheetMinutes ? w.CleanSheet : 0)
                         + goalsAgainst * w.GoalConceded * share;

        var defense = stats.Tackles * w.Tackle + stats.Interceptions * w.Interception + (isGoalkeeper ? 0 : resistance);
        var goalkeeping = stats.Saves * w.Save + (isGoalkeeper ? resistance : 0);

        var discipline = stats.RedCard ? RedCard : stats.YellowCards * YellowCard;

        var result = (goalsFor > goalsAgainst ? Win : goalsFor == goalsAgainst ? Draw : Loss) * share;

        var total = Math.Clamp(Base + attack + creation + defense + goalkeeping + discipline + result, Min, Max);

        return new RatingBreakdown(
            Round(attack, 2), Round(creation, 2), Round(defense, 2), Round(goalkeeping, 2),
            Round(discipline, 2), Round(result, 2), Round(total, 1));
    }

    private static Weights WeightsFor(Position? position) => position switch
    {
        Position.Goalkeeper => Goalkeeper,
        Position.Defender => Defender,
        Position.Forward => Forward,
        _ => Midfielder,
    };

    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}

/// <summary>Aportación de cada bloque a la nota (sobre la base de 6,0) y nota final.</summary>
public sealed record RatingBreakdown(
    double Attack,
    double Creation,
    double Defense,
    double Goalkeeping,
    double Discipline,
    double Result,
    double Total);
