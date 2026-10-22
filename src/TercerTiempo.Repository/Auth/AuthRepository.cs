using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.GetMyAccount;
using TercerTiempo.Application.Auth.Invite;
using TercerTiempo.Application.Auth.Login;
using TercerTiempo.Application.Auth.Register;
using TercerTiempo.Application.Squads;

namespace TercerTiempo.Repository.Auth;

internal sealed class AuthRepository(
    IQueryHandler<GetInviteQuery, SquadInviteDto> getInvite,
    ICommandHandler<RegisterCommand, AuthResponse> register,
    ICommandHandler<LoginCommand, AuthResponse> login,
    IQueryHandler<GetMyAccountQuery, AccountDto> getMyAccount) : IAuthRepository
{
    public Task<SquadInviteDto> GetInviteAsync(string inviteCode, CancellationToken cancellationToken = default) =>
        getInvite.Handle(new GetInviteQuery(inviteCode), cancellationToken);

    public Task<AuthResponse> RegisterAsync(RegisterCommand command, CancellationToken cancellationToken = default) =>
        register.Handle(command, cancellationToken);

    public Task<AuthResponse> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default) =>
        login.Handle(command, cancellationToken);

    public Task<AccountDto> GetMyAccountAsync(CancellationToken cancellationToken = default) =>
        getMyAccount.Handle(new GetMyAccountQuery(), cancellationToken);
}
