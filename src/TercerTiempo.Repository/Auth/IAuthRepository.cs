using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.RegisterPlayer;

namespace TercerTiempo.Repository.Auth;

/// <summary>Alta e inicio de sesión de jugadores.</summary>
public interface IAuthRepository
{
    Task<AuthResponse> RegisterAsync(RegisterPlayerCommand command, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);
}
