using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Matches;

public sealed record Score
{
    public int Home { get; }
    public int Away { get; }

    private Score(int home, int away)
    {
        Home = home;
        Away = away;
    }

    public static Score Create(int home, int away) =>
        new(Guard.NotNegative(home, "goles local"), Guard.NotNegative(away, "goles visitante"));

    public override string ToString() => $"{Home}-{Away}";
}
