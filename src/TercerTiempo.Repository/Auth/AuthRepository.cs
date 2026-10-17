using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.RegisterPlayer;

namespace TercerTiempo.Repository.Auth;

internal sealed class AuthRepository(
    ICommandHandler<RegisterPlayerCommand, AuthResponse> register,
    ICommandHandler<LoginCommand, AuthResponse> login) : IAuthRepository
{
    public Task<AuthResponse> RegisterAsync(RegisterPlayerCommand command, CancellationToken cancellationToken = default) =>
        register.Handle(command, cancellationToken);

    public Task<AuthResponse> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default) =>
        login.Handle(command, cancellationToken);
}
