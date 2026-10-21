namespace TercerTiempo.Application.Stats;

/// <summary>
/// Perfil de un jugador normalizado por 90 minutos (ejes del radar): permite comparar jugadores con distinto número de
/// minutos. La nota media y la participación van aparte porque ya son medias.
/// </summary>
public sealed record PlayerAxesDto(
    double GoalsPer90,
    double AssistsPer90,
    double ShotsOnTargetPer90,
    double KeyPassesPer90,
    double TacklesPer90,
    double InterceptionsPer90,
    double SavesPer90,
    double? AverageRating,
    double ParticipationPct)
{
    public static readonly PlayerAxesDto Zero = new(0, 0, 0, 0, 0, 0, 0, null, 0);
}

internal static class PlayerAxes
{
    /// <summary>Minutos mínimos para que un jugador cuente en la media y el máximo de la plantilla.</summary>
    public const int MinMinutesForReference = 90;

    public static PlayerAxesDto From(IndividualStatsDto stats, TeamImpactDto impact)
    {
        if (stats.Minutes <= 0) return PlayerAxesDto.Zero with { ParticipationPct = impact.ParticipationPct };

        double Per90(int value) => Math.Round(value * 90.0 / stats.Minutes, 2);

        return new PlayerAxesDto(
            Per90(stats.Goals),
            Per90(stats.Assists),
            Per90(stats.ShotsOnTarget),
            Per90(stats.KeyPasses),
            Per90(stats.Tackles),
            Per90(stats.Interceptions),
            Per90(stats.Saves),
            stats.AverageRating,
            impact.ParticipationPct);
    }

    public static PlayerAxesDto Average(IReadOnlyCollection<PlayerAxesDto> axes)
    {
        if (axes.Count == 0) return PlayerAxesDto.Zero;

        double Avg(Func<PlayerAxesDto, double> selector) => Math.Round(axes.Average(selector), 2);
        var ratings = axes.Where(a => a.AverageRating is not null).Select(a => a.AverageRating!.Value).ToList();

        return new PlayerAxesDto(
            Avg(a => a.GoalsPer90), Avg(a => a.AssistsPer90), Avg(a => a.ShotsOnTargetPer90), Avg(a => a.KeyPassesPer90),
            Avg(a => a.TacklesPer90), Avg(a => a.InterceptionsPer90), Avg(a => a.SavesPer90),
            ratings.Count > 0 ? Math.Round(ratings.Average(), 2) : null,
            Avg(a => a.ParticipationPct));
    }

    public static PlayerAxesDto Max(IReadOnlyCollection<PlayerAxesDto> axes)
    {
        if (axes.Count == 0) return PlayerAxesDto.Zero;

        double Top(Func<PlayerAxesDto, double> selector) => axes.Max(selector);
        var ratings = axes.Where(a => a.AverageRating is not null).Select(a => a.AverageRating!.Value).ToList();

        return new PlayerAxesDto(
            Top(a => a.GoalsPer90), Top(a => a.AssistsPer90), Top(a => a.ShotsOnTargetPer90), Top(a => a.KeyPassesPer90),
            Top(a => a.TacklesPer90), Top(a => a.InterceptionsPer90), Top(a => a.SavesPer90),
            ratings.Count > 0 ? ratings.Max() : null,
            Top(a => a.ParticipationPct));
    }
}
