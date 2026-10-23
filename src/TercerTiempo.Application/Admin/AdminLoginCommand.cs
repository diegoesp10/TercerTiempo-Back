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

public sealed record AdminAuthResponse(string AccessToken, DateTimeOffset ExpiresAt, string UserName);

internal sealed class AdminLoginValidator : AbstractValidator<AdminLoginCommand>
{
    public AdminLoginValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class AdminLoginHandler(IIdentityService identity, ITokenService tokens) : ICommandHandler<AdminLoginCommand, AdminAuthResponse>
{
    public async Task<AdminAuthResponse> Handle(AdminLoginCommand command, CancellationToken cancellationToken)
    {
        var userName = command.UserName.Trim();
        var userId = await identity.ValidateAdminAsync(userName, command.Password, cancellationToken)
            ?? throw new InvalidCredentialsException(ErrorCode.AdminInvalidCredentials);

        var token = tokens.CreateAdminToken(userId, userName);
        return new AdminAuthResponse(token.AccessToken, token.ExpiresAt, userName);
    }
}
