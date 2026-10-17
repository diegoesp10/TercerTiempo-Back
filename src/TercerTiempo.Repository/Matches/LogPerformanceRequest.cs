using TercerTiempo.Application.Performances.LogPerformance;

namespace TercerTiempo.Repository.Matches;

/// <summary>Datos de rendimiento que envía el cliente; el partido llega por la ruta.</summary>
public sealed record LogPerformanceRequest(
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
    int SelfRating,
    string? Notes)
{
    internal LogPerformanceCommand ToCommand(Guid matchId) => new(
        matchId, Started, MinutesPlayed, Goals, Assists, Shots, ShotsOnTarget, KeyPasses,
        Tackles, Interceptions, Saves, YellowCards, RedCard, SelfRating, Notes);
}
