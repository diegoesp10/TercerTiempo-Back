using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Coaches;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Auth.Register;

/// <summary>Alta en la plataforma como jugador o como miembro del cuerpo técnico.</summary>
public sealed record RegisterCommand(
    [property: Description("Email de acceso. Debe ser único.")]
    string Email,
    [property: Description("Mínimo 8 caracteres y al menos un número.")]
    string Password,
    [property: Description("Nombre y apellidos. Si eres jugador, tal y como aparecen en la plantilla de tu equipo.")]
    string DisplayName,
    [property: Description("Player (jugador) o Coach (cuerpo técnico: entrenador, ayudante, delegado...).")]
    AccountType AccountType,
    [property: Description("Solo jugadores: Goalkeeper, Defender, Midfielder o Forward.")]
    Position? Position,
    [property: Description("Opcional, formato yyyy-MM-dd. Jugadores: mínimo 14 años.")]
    DateOnly? BirthDate) : ICommand<AuthResponse>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountType).IsInEnum();
        RuleFor(x => x.Position).NotNull().When(x => x.AccountType == AccountType.Player)
            .WithMessage("Indica tu posición.");
        RuleFor(x => x.Position).IsInEnum().When(x => x.Position.HasValue);
    }
}

internal sealed class RegisterHandler(
    IIdentityService identity,
    IPlayerStore players,
    ICoachStore coaches,
    IUnitOfWork unitOfWork,
    ITokenService tokens,
    TimeProvider clock) : ICommandHandler<RegisterCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        var now = clock.GetUtcNow();

        var userId = await identity.CreateUserAsync(email, command.Password, cancellationToken);

        AccountDto account;
        Guid profileId;

        if (command.AccountType == AccountType.Player)
        {
            var player = Player.Register(userId, command.DisplayName, command.Position!.Value, command.BirthDate, now);
            players.Add(player);
            profileId = player.Id;
            account = new AccountDto(AccountType.Player, PlayerMapper.ToDto(player, null, null, null, null), null);
        }
        else
        {
            var coach = Coach.Register(userId, command.DisplayName, now);
            coaches.Add(coach);
            profileId = coach.Id;
            account = new AccountDto(AccountType.Coach, null, new CoachDto(coach.Id, coach.DisplayName, []));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokens.CreateToken(userId, email, command.AccountType, profileId);
        return new AuthResponse(token.AccessToken, token.ExpiresAt, account);
    }
}
