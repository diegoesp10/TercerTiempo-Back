using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
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
    ICompetitionStore competitions,
    ITeamStore teams,
    ITokenService tokens) : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();

        var userId = await identity.ValidateCredentialsAsync(email, command.Password, cancellationToken)
            ?? throw new InvalidCredentialsException();

        var player = await players.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Jugador", userId);

        var token = tokens.CreateToken(userId, email, player.Id);
        var dto = await PlayerMapper.ToDtoAsync(player, competitions, teams, cancellationToken);

        return new AuthResponse(token.AccessToken, token.ExpiresAt, dto);
    }
}
