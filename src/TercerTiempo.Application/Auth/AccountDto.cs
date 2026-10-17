using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Auth;

/// <summary>Cuenta autenticada: perfil de jugador o de técnico, con sus plantillas.</summary>
public sealed record AccountDto(AccountType Type, PlayerDto? Player, CoachDto? Coach);

internal static class AccountResolver
{
    public static async Task<(AccountType Type, Guid ProfileId)> IdentifyAsync(
        Guid userId, IPlayerStore players, ICoachStore coaches, CancellationToken cancellationToken)
    {
        if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player)
            return (AccountType.Player, player.Id);

        if (await coaches.GetByUserIdAsync(userId, cancellationToken) is { } coach)
            return (AccountType.Coach, coach.Id);

        throw new NotFoundException("Cuenta", userId);
    }

    public static async Task<AccountDto> ForUserAsync(
        Guid userId,
        IPlayerStore players,
        ICoachStore coaches,
        ISquadStore squads,
        ITeamStore teams,
        ICompetitionStore competitions,
        CancellationToken cancellationToken)
    {
        if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player)
            return new AccountDto(AccountType.Player, await PlayerMapper.ToDtoAsync(player, squads, teams, competitions, cancellationToken), null);

        var coach = await coaches.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Cuenta", userId);

        return new AccountDto(AccountType.Coach, null, await CoachMapper.ToDtoAsync(coach, squads, teams, competitions, cancellationToken));
    }
}
