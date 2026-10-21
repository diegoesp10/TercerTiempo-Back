using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;

namespace TercerTiempo.Application.Auth.GetMyAccount;

/// <summary>Cuenta autenticada con su perfil: plantillas y nombramientos del técnico, equipos y nombramientos del club.</summary>
public sealed record GetMyAccountQuery : IQuery<AccountDto>;

internal sealed class GetMyAccountHandler(ICurrentUser currentUser, AccountStores stores) : IQueryHandler<GetMyAccountQuery, AccountDto>
{
    public Task<AccountDto> Handle(GetMyAccountQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        return AccountResolver.ForUserAsync(userId, stores, cancellationToken);
    }
}
