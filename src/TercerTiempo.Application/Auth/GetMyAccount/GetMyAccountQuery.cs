using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Auth.GetMyAccount;

/// <summary>Cuenta autenticada con su perfil y sus plantillas.</summary>
public sealed record GetMyAccountQuery : IQuery<AccountDto>;

internal sealed class GetMyAccountHandler(
    ICurrentUser currentUser,
    IPlayerStore players,
    ICoachStore coaches,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions) : IQueryHandler<GetMyAccountQuery, AccountDto>
{
    public Task<AccountDto> Handle(GetMyAccountQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return AccountResolver.ForUserAsync(userId, players, coaches, squads, teams, competitions, cancellationToken);
    }
}
