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
    string? Notes,
    int Fouls = 0,
    int BallLosses = 0,
    int Recoveries = 0,
    int PassesCompleted = 0,
    int PassesAttempted = 0)
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
        Guard.NotNegative(Fouls, "faltas");
        Guard.NotNegative(BallLosses, "pérdidas de balón");
        Guard.NotNegative(Recoveries, "recuperaciones de balón");
        Guard.NotNegative(PassesCompleted, "pases completados");
        Guard.NotNegative(PassesAttempted, "pases intentados");

        if (ShotsOnTarget > Shots)
            throw new DomainException(ErrorCode.SheetShotsOnTarget);

        if (PassesCompleted > PassesAttempted)
            throw new DomainException(ErrorCode.SheetPassesCompleted);

        if (YellowCards == 2 && !RedCard)
            throw new DomainException(ErrorCode.SheetTwoYellows);

        if (MinutesPlayed == 0 && (Started || Goals > 0 || Assists > 0 || Shots > 0 || KeyPasses > 0
                                   || Tackles > 0 || Interceptions > 0 || Saves > 0 || YellowCards > 0 || RedCard || Fouls > 0
                                   || BallLosses > 0 || Recoveries > 0 || PassesAttempted > 0))
            throw new DomainException(ErrorCode.SheetNoMinutes);

        if (Notes is { Length: > MaxNotesLength })
            throw new DomainException(ErrorCode.SheetNotesTooLong, MaxNotesLength);
    }
}
