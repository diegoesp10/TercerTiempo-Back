using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.GetMyAccount;
using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.Register;

namespace TercerTiempo.Repository.Auth;

internal sealed class AuthRepository(
    ICommandHandler<RegisterCommand, AuthResponse> register,
    ICommandHandler<LoginCommand, AuthResponse> login,
    IQueryHandler<GetMyAccountQuery, AccountDto> getMyAccount) : IAuthRepository
{
    public Task<AuthResponse> RegisterAsync(RegisterCommand command, CancellationToken cancellationToken = default) =>
        register.Handle(command, cancellationToken);

    public Task<AuthResponse> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default) =>
        login.Handle(command, cancellationToken);

    public Task<AccountDto> GetMyAccountAsync(CancellationToken cancellationToken = default) =>
        getMyAccount.Handle(new GetMyAccountQuery(), cancellationToken);
}
