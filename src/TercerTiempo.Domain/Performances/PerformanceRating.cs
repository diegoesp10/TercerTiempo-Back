using TercerTiempo.Domain.Players;

namespace TercerTiempo.Domain.Performances;

public static class PerformanceRating
{
    public const double Base = 6.0;
    public const double Min = 3.0;
    public const double Max = 10.0;

    public const int CleanSheetMinutes = 60;

    private sealed record Weights(
        double Goal, double Assist, double KeyPass, double ShotOnTarget,
        double Tackle, double Interception, double Save,
        double CleanSheet, double GoalConceded, double Recovery);

    private static readonly Weights Goalkeeper = new(1.5, 0.8, 0.25, 0.10, 0.10, 0.10, 0.35, 1.0, -0.30, 0.04);
    private static readonly Weights Defender = new(1.2, 0.8, 0.20, 0.10, 0.15, 0.15, 0.30, 0.6, -0.20, 0.08);
    private static readonly Weights Midfielder = new(1.0, 0.7, 0.20, 0.12, 0.12, 0.12, 0.30, 0.2, -0.05, 0.07);
    private static readonly Weights Forward = new(0.9, 0.7, 0.15, 0.15, 0.10, 0.08, 0.30, 0.0, 0.00, 0.05);

    private const double BallLoss = -0.05;

    // El acierto en el pase solo cuenta con un mínimo de intentos: por encima del 75 % suma y por debajo resta, con tope.
    private const int MinPassesForAccuracy = 10;
    private const double ReferencePassAccuracy = 0.75;
    private const double MaxPassAccuracyEffect = 0.3;

    private const double YellowCard = -0.3;
    private const double RedCard = -1.5;
    private const double Win = 0.4;
    private const double Draw = 0.1;
    private const double Loss = -0.3;

    public static RatingBreakdown? Calculate(PerformanceStats stats, Position? position, int goalsFor, int goalsAgainst)
    {
        if (stats.MinutesPlayed <= 0) return null;

        var w = WeightsFor(position);
        var isGoalkeeper = position == Position.Goalkeeper;
        var share = Math.Min(stats.MinutesPlayed, 90) / 90.0;

        var attack = stats.Goals * w.Goal + stats.ShotsOnTarget * w.ShotOnTarget;
        var passing = stats.PassesAttempted >= MinPassesForAccuracy
            ? Math.Clamp((stats.PassesCompleted / (double)stats.PassesAttempted - ReferencePassAccuracy) * 1.2, -MaxPassAccuracyEffect, MaxPassAccuracyEffect)
            : 0;
        var creation = stats.Assists * w.Assist + stats.KeyPasses * w.KeyPass + passing + stats.BallLosses * BallLoss;

        var resistance = (goalsAgainst == 0 && stats.MinutesPlayed >= CleanSheetMinutes ? w.CleanSheet : 0)
                         + goalsAgainst * w.GoalConceded * share;

        var defense = stats.Tackles * w.Tackle + stats.Interceptions * w.Interception + stats.Recoveries * w.Recovery + (isGoalkeeper ? 0 : resistance);
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

public sealed record RatingBreakdown(
    double Attack,
    double Creation,
    double Defense,
    double Goalkeeping,
    double Discipline,
    double Result,
    double Total);
