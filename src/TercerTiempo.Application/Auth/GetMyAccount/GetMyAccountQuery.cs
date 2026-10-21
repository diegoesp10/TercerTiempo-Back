using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;

namespace TercerTiempo.Application.Auth.GetMyAccount;

/// <summary>Cuenta autenticada con su perfil: plantillas y nombramientos del técnico, equipos y nombramientos del club.</summary>
public sealed record GetMyAccountQuery : IQuery<AccountDto>;

internal sealed class GetMyAccountHandler(ICurrentUser currentUser, AccountStores stores) : IQueryHandler<GetMyAccountQuery, AccountDto>
{
    public async Task<AccountDto> Handle(GetMyAccountQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        try
        {
            return await AccountResolver.ForUserAsync(userId, stores, cancellationToken);
        }
        catch (NotFoundException ex) when (ex.Entity == "Cuenta")
        {
            // El token es válido pero su usuario ya no existe (base de datos recreada, cuenta borrada):
            // se responde 401 para que el cliente descarte la sesión guardada en lugar de un 404.
            throw new UnauthenticatedException();
        }
    }
}
