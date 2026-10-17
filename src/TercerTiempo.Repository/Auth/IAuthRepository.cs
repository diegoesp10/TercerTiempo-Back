using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.Register;

namespace TercerTiempo.Repository.Auth;

/// <summary>Alta, inicio de sesión y cuenta autenticada (jugador o cuerpo técnico).</summary>
public interface IAuthRepository
{
    Task<AuthResponse> RegisterAsync(RegisterCommand command, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);
    Task<AccountDto> GetMyAccountAsync(CancellationToken cancellationToken = default);
}
