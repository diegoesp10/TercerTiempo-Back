using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Performances;

public sealed record PerformanceStats(
    bool Started,
    int MinutesPlayed,
    int Goals,
    int Assists,
    int Shots,
    int ShotsOnTarget,
    int KeyPasses,
    int Tackles,
    int Interceptions,
    int Saves,
    int YellowCards,
    bool RedCard,
    string? Notes)
{
    public const int MaxMinutes = 120;
    public const int MaxNotesLength = 1000;

    public void EnsureValid()
    {
        Guard.InRange(MinutesPlayed, 0, MaxMinutes, "minutos");
        Guard.NotNegative(Goals, "goles");
        Guard.NotNegative(Assists, "asistencias");
        Guard.NotNegative(Shots, "tiros");
        Guard.NotNegative(ShotsOnTarget, "tiros a puerta");
        Guard.NotNegative(KeyPasses, "pases clave");
        Guard.NotNegative(Tackles, "entradas");
        Guard.NotNegative(Interceptions, "intercepciones");
        Guard.NotNegative(Saves, "paradas");
        Guard.InRange(YellowCards, 0, 2, "amarillas");

        if (ShotsOnTarget > Shots)
            throw new DomainException(ErrorCode.SheetShotsOnTarget);

        if (YellowCards == 2 && !RedCard)
            throw new DomainException(ErrorCode.SheetTwoYellows);

        if (MinutesPlayed == 0 && (Started || Goals > 0 || Assists > 0 || Shots > 0 || KeyPasses > 0
                                   || Tackles > 0 || Interceptions > 0 || Saves > 0 || YellowCards > 0 || RedCard))
            throw new DomainException(ErrorCode.SheetNoMinutes);

        if (Notes is { Length: > MaxNotesLength })
            throw new DomainException(ErrorCode.SheetNotesTooLong, MaxNotesLength);
    }
}
