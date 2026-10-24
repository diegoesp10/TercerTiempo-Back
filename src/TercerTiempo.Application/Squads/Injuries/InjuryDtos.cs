using System.ComponentModel;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record InjuryTypeDto(
    InjuryType Type,
    string Label,
    [property: Description("Duración media orientativa en días; null si no hay referencia (Otra).")]
    int? AverageDays);

public sealed record InjuryDto(
    Guid Id,
    Guid SquadPlayerId,
    string PlayerName,
    int? ShirtNumber,
    InjuryType Type,
    string TypeLabel,
    int? AverageDays,
    string? Reason,
    DateOnly StartedOn,
    DateOnly? ExpectedReturnOn,
    DateOnly? ReturnedOn,
    bool Active,
    [property: Description("Días de baja hasta hoy (o hasta el alta si ya la tiene).")]
    int DaysOut);

internal static class InjuryMapper
{
    public static InjuryTypeDto ToDto(InjuryTypeInfo info) => new(info.Type, info.Label, info.AverageDays);

    public static InjuryDto ToDto(PlayerInjury injury, SquadPlayer? player, DateOnly today)
    {
        var info = InjuryCatalog.Info(injury.Type);
        return new InjuryDto(
            injury.Id, injury.SquadPlayerId, player?.FullName ?? "Jugador", player?.ShirtNumber,
            injury.Type, info.Label, info.AverageDays, injury.Reason,
            injury.StartedOn, injury.ExpectedReturnOn, injury.ReturnedOn, injury.IsActive, injury.DaysOut(today));
    }

    public static IReadOnlyList<InjuryDto> ToList(IEnumerable<PlayerInjury> injuries, Squad squad, DateOnly today)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        return injuries
            .OrderByDescending(i => i.IsActive)
            .ThenByDescending(i => i.StartedOn)
            .Select(i => ToDto(i, players.GetValueOrDefault(i.SquadPlayerId), today))
            .ToList();
    }

    public static IReadOnlyDictionary<Guid, InjuryDto> ActiveByPlayer(IEnumerable<PlayerInjury> injuries, Squad squad, DateOnly today)
    {
        var players = squad.Players.ToDictionary(p => p.Id);
        return injuries
            .Where(i => i.IsActive)
            .GroupBy(i => i.SquadPlayerId)
            .ToDictionary(g => g.Key, g => ToDto(g.OrderByDescending(i => i.StartedOn).First(), players.GetValueOrDefault(g.Key), today));
    }
}
