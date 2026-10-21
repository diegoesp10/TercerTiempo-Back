using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;

namespace TercerTiempo.Application.Auth.Login;

public sealed record LoginCommand(
    [property: Description("Email con el que te diste de alta.")]
    string Email,
    [property: Description("Contraseña.")]
    string Password) : ICommand<AuthResponse>;

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class LoginHandler(
    IIdentityService identity,
    AccountStores stores,
    ITokenService tokens) : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();

        var userId = await identity.ValidateCredentialsAsync(email, command.Password, cancellationToken)
            ?? throw new InvalidCredentialsException();

        var (type, profileId) = await AccountResolver.IdentifyAsync(userId, stores.Players, stores.Coaches, stores.Clubs, cancellationToken);
        var token = tokens.CreateToken(userId, email, type, profileId);
        var account = await AccountResolver.ForUserAsync(userId, stores, cancellationToken);

        return new AuthResponse(token.AccessToken, token.ExpiresAt, account);
    }
}
