using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Auth.ChangePassword;

public sealed record ChangePasswordCommand(
    [property: Description("Contraseña actual de la cuenta.")]
    string CurrentPassword,
    [property: Description("Contraseña nueva: al menos 10 caracteres con mayúscula, minúscula, número y símbolo, distinta de la actual.")]
    string NewPassword,
    [property: Description("La contraseña nueva otra vez: tienen que coincidir.")]
    string ConfirmNewPassword) : ICommand<AuthResponse>;

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.NewPassword).NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength).WithCatalogMessage(ErrorCode.AuthPasswordTooShort, PasswordPolicy.MinLength)
            .MaximumLength(PasswordPolicy.MaxLength).WithCatalogMessage(ErrorCode.AuthPasswordTooLong, PasswordPolicy.MaxLength)
            .NotEqual(x => x.CurrentPassword).WithCatalogMessage(ErrorCode.AuthPasswordSame);
        RuleFor(x => x.ConfirmNewPassword).Equal(x => x.NewPassword).WithCatalogMessage(ErrorCode.AuthPasswordMismatch);
    }
}

internal sealed class ChangePasswordHandler(
    ICurrentUser currentUser,
    IIdentityService identity,
    AccountStores stores,
    ITokenService tokens) : ICommandHandler<ChangePasswordCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        var email = await identity.ChangePasswordAsync(userId, command.CurrentPassword, command.NewPassword, cancellationToken);

        // La contraseña nueva cierra las demás sesiones; la que la ha cambiado sigue abierta con un token nuevo.
        var (type, profileId) = await AccountResolver.IdentifyAsync(userId, stores.Players, stores.Coaches, stores.Clubs, cancellationToken);
        var token = tokens.CreateToken(userId, email, type, profileId, await identity.GetSessionStampAsync(userId, cancellationToken));
        var account = await AccountResolver.ForUserAsync(userId, stores, cancellationToken);
        return new AuthResponse(token.AccessToken, token.ExpiresAt, account);
    }
}

public sealed record PasswordStatusDto(
    [property: Description("Último cambio de contraseña (null si no se ha cambiado desde el alta).")]
    DateTimeOffset? ChangedAt,
    [property: Description("Desde cuándo se puede volver a cambiar (null si ya se puede).")]
    DateTimeOffset? NextChangeAt,
    bool CanChange);

public sealed record GetPasswordStatusQuery : IQuery<PasswordStatusDto>;

internal sealed class GetPasswordStatusHandler(ICurrentUser currentUser, IIdentityService identity, TimeProvider clock)
    : IQueryHandler<GetPasswordStatusQuery, PasswordStatusDto>
{
    public async Task<PasswordStatusDto> Handle(GetPasswordStatusQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
            throw new UnauthenticatedException();

        var state = await identity.GetPasswordStateAsync(userId, cancellationToken);
        var canChange = state.NextChangeAt is not { } next || next <= clock.GetUtcNow();
        return new PasswordStatusDto(state.ChangedAt, canChange ? null : state.NextChangeAt, canChange);
    }
}
