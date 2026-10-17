using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Players;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Auth.RegisterPlayer;

/// <summary>Alta de un jugador en la plataforma: crea el usuario de identidad y el agregado Player.</summary>
public sealed record RegisterPlayerCommand(
    [property: Description("Email de acceso. Debe ser único.")]
    string Email,
    [property: Description("Mínimo 8 caracteres y al menos un número.")]
    string Password,
    [property: Description("Nombre y apellidos tal y como aparecen en la plantilla del equipo (sirve para verificarte automáticamente).")]
    string DisplayName,
    [property: Description("Posición principal: Goalkeeper, Defender, Midfielder o Forward.")]
    Position Position,
    [property: Description("Opcional, formato yyyy-MM-dd. Mínimo 14 años.")]
    DateOnly? BirthDate) : ICommand<AuthResponse>;

internal sealed class RegisterPlayerValidator : AbstractValidator<RegisterPlayerCommand>
{
    public RegisterPlayerValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Position).IsInEnum();
    }
}

internal sealed class RegisterPlayerHandler(
    IIdentityService identity,
    IPlayerStore players,
    IUnitOfWork unitOfWork,
    ITokenService tokens,
    TimeProvider clock) : ICommandHandler<RegisterPlayerCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterPlayerCommand command, CancellationToken cancellationToken)
    {
        var userId = await identity.CreateUserAsync(command.Email.Trim(), command.Password, cancellationToken);

        var player = Player.Register(userId, command.DisplayName, command.Position, command.BirthDate, clock.GetUtcNow());
        players.Add(player);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokens.CreateToken(userId, command.Email.Trim(), player.Id);
        return new AuthResponse(token.AccessToken, token.ExpiresAt, PlayerMapper.ToDto(player, null, null, null, null));
    }
}
