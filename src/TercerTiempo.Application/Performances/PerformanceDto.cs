using TercerTiempo.Application.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Performances;

/// <summary>Línea del acta de un partido para una ficha de la plantilla.</summary>
public sealed record PerformanceDto(
    Guid Id,
    Guid MatchId,
    Guid SquadId,
    Guid SquadPlayerId,
    string PlayerName,
    Position? Position,
    [property: System.ComponentModel.Description("Dorsal con el que jugó: el de este partido si se registró, si no el de su ficha.")]
    int? ShirtNumber,
    [property: System.ComponentModel.Description("Dorsal registrado solo para este partido (null = el de la ficha).")]
    int? MatchShirtNumber,
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
    double? Rating,
    string? Notes,
    DateTimeOffset UpdatedAt);

public sealed record PerformanceWithMatchDto(PerformanceDto Performance, MatchDto Match);

internal static class PerformanceMapper
{
    public static PerformanceDto ToDto(PlayerMatchPerformance p, SquadPlayer? player) =>
        new(p.Id, p.MatchId, p.SquadId, p.SquadPlayerId,
            player?.FullName ?? "Jugador", player?.Position, p.ShirtNumber ?? player?.ShirtNumber, p.ShirtNumber,
            p.TeamId, p.Started, p.MinutesPlayed, p.Goals, p.Assists, p.Shots, p.ShotsOnTarget,
            p.KeyPasses, p.Tackles, p.Interceptions, p.Saves, p.YellowCards, p.RedCard,
            p.Rating, p.Notes, p.UpdatedAt);
}
