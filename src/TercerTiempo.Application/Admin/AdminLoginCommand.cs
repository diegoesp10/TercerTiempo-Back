using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Admin;

public sealed record AdminLoginCommand(
    [property: Description("Nombre de usuario del administrador (Admin:UserName).")]
    string UserName,
    [property: Description("Contraseña.")]
    string Password) : ICommand<AdminAuthResponse>;

public sealed record AdminVerifyMfaCommand(
    [property: Description("El billete que devolvió el login (mfa.ticket).")]
    string Ticket,
    [property: Description("Código de 6 cifras de la app de autenticación (Microsoft Authenticator o cualquier TOTP).")]
    string Code) : ICommand<AdminAuthResponse>;

// Sin segundo paso, el token. Con el MFA de publicación, el login no da token: da el reto (Mfa) y el token llega al
// verificar el código.
public sealed record AdminAuthResponse(string? AccessToken, DateTimeOffset? ExpiresAt, string UserName, AdminMfaChallenge? Mfa = null);

public sealed record AdminMfaChallenge(
    [property: Description("Billete de un solo uso que prueba que la contraseña era buena; caduca en 5 minutos.")]
    string Ticket,
    DateTimeOffset ExpiresAt,
    [property: Description("Solo la primera vez (o tras Admin:ResetMfa): la clave para dar de alta la cuenta en la app.")]
    AdminMfaSetup? Setup);

public sealed record AdminMfaSetup(
    [property: Description("Clave secreta en base32, en grupos de 4, para escribirla a mano en la app.")]
    string SharedKey,
    [property: Description("otpauth://totp/... para el código QR.")]
    string AuthenticatorUri);

internal sealed class AdminLoginValidator : AbstractValidator<AdminLoginCommand>
{
    public AdminLoginValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class AdminVerifyMfaValidator : AbstractValidator<AdminVerifyMfaCommand>
{
    public AdminVerifyMfaValidator()
    {
        RuleFor(x => x.Ticket).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
    }
}

internal sealed class AdminLoginHandler(IIdentityService identity, ITokenService tokens, IAdminSecondFactor secondFactor)
    : ICommandHandler<AdminLoginCommand, AdminAuthResponse>
{
    public async Task<AdminAuthResponse> Handle(AdminLoginCommand command, CancellationToken cancellationToken)
    {
        var userName = command.UserName.Trim();
        var userId = await identity.ValidateAdminAsync(userName, command.Password, secondFactor.Required, cancellationToken)
            ?? throw new InvalidCredentialsException(ErrorCode.AdminInvalidCredentials);

        if (secondFactor.Required)
            return new AdminAuthResponse(null, null, userName, await secondFactor.ChallengeAsync(userId, userName, cancellationToken));

        var token = tokens.CreateAdminToken(userId, userName, await identity.GetSessionStampAsync(userId, cancellationToken));
        return new AdminAuthResponse(token.AccessToken, token.ExpiresAt, userName);
    }
}

internal sealed class AdminVerifyMfaHandler(IIdentityService identity, ITokenService tokens, IAdminSecondFactor secondFactor)
    : ICommandHandler<AdminVerifyMfaCommand, AdminAuthResponse>
{
    public async Task<AdminAuthResponse> Handle(AdminVerifyMfaCommand command, CancellationToken cancellationToken)
    {
        var admin = await secondFactor.VerifyAsync(command.Ticket, command.Code, cancellationToken);
        var token = tokens.CreateAdminToken(admin.UserId, admin.UserName, await identity.GetSessionStampAsync(admin.UserId, cancellationToken));
        return new AdminAuthResponse(token.AccessToken, token.ExpiresAt, admin.UserName);
    }
}
