using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.Register;
using TercerTiempo.Application.Squads;

namespace TercerTiempo.Repository.Auth;

public interface IAuthRepository
{
    Task<SquadInviteDto> GetInviteAsync(string inviteCode, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(RegisterCommand command, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);
    Task<AccountDto> GetMyAccountAsync(CancellationToken cancellationToken = default);
}
