using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

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
    IPlayerStore players,
    ICoachStore coaches,
    ISquadStore squads,
    ITeamStore teams,
    ICompetitionStore competitions,
    ITokenService tokens) : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();

        var userId = await identity.ValidateCredentialsAsync(email, command.Password, cancellationToken)
            ?? throw new InvalidCredentialsException();

        var (type, profileId) = await AccountResolver.IdentifyAsync(userId, players, coaches, cancellationToken);
        var token = tokens.CreateToken(userId, email, type, profileId);
        var account = await AccountResolver.ForUserAsync(userId, players, coaches, squads, teams, competitions, cancellationToken);

        return new AuthResponse(token.AccessToken, token.ExpiresAt, account);
    }
}
