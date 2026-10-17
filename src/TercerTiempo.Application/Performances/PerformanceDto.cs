using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Performances;

public sealed record PerformanceDto(
    Guid Id,
    Guid MatchId,
    Guid PlayerId,
    string PlayerName,
    Position PlayerPosition,
    Guid TeamId,
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
    string? Notes,
    DateTimeOffset UpdatedAt);

public sealed record PerformanceWithMatchDto(PerformanceDto Performance, MatchDto Match);

internal static class PerformanceMapper
{
    public static PerformanceDto ToDto(PlayerMatchPerformance p, Player? player) =>
        new(p.Id, p.MatchId, p.PlayerId,
            player?.DisplayName ?? "Jugador", player?.Position ?? Position.Midfielder,
            p.TeamId, p.Started, p.MinutesPlayed, p.Goals, p.Assists, p.Shots, p.ShotsOnTarget,
            p.KeyPasses, p.Tackles, p.Interceptions, p.Saves, p.YellowCards, p.RedCard,
            p.SelfRating, p.Notes, p.UpdatedAt);
}
