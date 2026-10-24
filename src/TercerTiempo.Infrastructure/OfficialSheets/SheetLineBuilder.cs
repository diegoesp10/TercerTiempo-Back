using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Infrastructure.OfficialSheets;

// Una línea del acta mientras se lee la página de cualquier web: entradas, salidas, goles y tarjetas.
internal sealed class SheetLineBuilder(MatchSide side, int? shirt, string name, bool started, int matchLength)
{
    public MatchSide Side { get; } = side;
    public int? Shirt { get; } = shirt;
    public string Name { get; } = name;
    public string NormalizedName { get; } = NameNormalizer.Normalize(name.Replace(',', ' '));
    public bool Started { get; } = started;
    public int? MinuteIn { get; private set; } = started ? 0 : null;
    public int? MinuteOut { get; private set; }
    public int Goals { get; set; }
    public int PenaltyGoals { get; set; }
    public int OwnGoals { get; set; }
    public int YellowCards { get; private set; }
    public bool RedCard { get; private set; }

    public void EnterAt(int minute) => MinuteIn ??= minute;

    public void LeaveAt(int minute) => MinuteOut ??= minute;

    public void Yellow(int minute)
    {
        YellowCards = Math.Min(2, YellowCards + 1);
        if (YellowCards == 2)
        {
            RedCard = true;
            MinuteOut ??= minute;
        }
    }

    public void Red(int minute)
    {
        RedCard = true;
        MinuteOut ??= minute;
    }

    public OfficialLineData Build()
    {
        var minutes = MinuteIn is { } @in ? Math.Max(0, (MinuteOut ?? Math.Max(matchLength, @in)) - @in) : 0;
        return new OfficialLineData(Side, Shirt, Name, Started, MinuteIn, MinuteOut, minutes, Goals, PenaltyGoals, OwnGoals, YellowCards, RedCard);
    }
}
