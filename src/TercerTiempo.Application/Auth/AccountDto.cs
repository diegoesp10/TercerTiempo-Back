using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Clubs;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Auth;

/// <summary>Cuenta autenticada: perfil de jugador, de técnico (con sus plantillas y nombramientos) o de club.</summary>
public sealed record AccountDto(AccountType Type, PlayerDto? Player, CoachDto? Coach, ClubDto? Club);

/// <summary>Almacenes necesarios para montar la cuenta de cualquier tipo de usuario.</summary>
internal sealed record AccountStores(
    IPlayerStore Players,
    ICoachStore Coaches,
    IClubStore Clubs,
    ICoachAppointmentStore Appointments,
    ISquadStore Squads,
    ITeamStore Teams,
    ICompetitionStore Competitions,
    TimeProvider Clock);

internal static class AccountResolver
{
    public static async Task<(AccountType Type, Guid ProfileId)> IdentifyAsync(
        Guid userId, IPlayerStore players, ICoachStore coaches, IClubStore clubs, CancellationToken cancellationToken)
    {
        if (await players.GetByUserIdAsync(userId, cancellationToken) is { } player)
            return (AccountType.Player, player.Id);

        if (await coaches.GetByUserIdAsync(userId, cancellationToken) is { } coach)
            return (AccountType.Coach, coach.Id);

        if (await clubs.GetByUserIdAsync(userId, cancellationToken) is { } club)
            return (AccountType.Club, club.Id);

        throw new NotFoundException("Cuenta", userId);
    }

    public static async Task<AccountDto> ForUserAsync(Guid userId, AccountStores stores, CancellationToken cancellationToken)
    {
        if (await stores.Players.GetByUserIdAsync(userId, cancellationToken) is { } player)
            return new AccountDto(AccountType.Player, await PlayerMapper.ToDtoAsync(player, stores.Squads, stores.Teams, stores.Competitions, cancellationToken), null, null);

        if (await stores.Coaches.GetByUserIdAsync(userId, cancellationToken) is { } coach)
            return new AccountDto(AccountType.Coach, null, await CoachMapper.ToDtoAsync(coach, stores, cancellationToken), null);

        var club = await stores.Clubs.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Cuenta", userId);

        return new AccountDto(AccountType.Club, null, null, await ClubMapper.ToDtoAsync(club, stores, cancellationToken));
    }
}
