using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

namespace TercerTiempo.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager, SessionStamps sessions, TimeProvider clock) : IIdentityService
{
    private static readonly TimeZoneInfo SiteZone = SiteClock.Resolve("Europe/Madrid");

    public async Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
            throw new ConflictException(ErrorCode.AuthEmailTaken);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded) throw Failures(result, passwordField: "password");

        return user.Id;
    }

    public async Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return null;

        // El administrador solo entra por /api/admin/login: su sesión nunca es la de una cuenta de club, técnico o jugador.
        if (await userManager.IsInRoleAsync(user, AuthClaims.AdminRole)) return null;

        if (await LockedMinutesAsync(user) is { } locked) throw new InvalidCredentialsException(ErrorCode.AuthLockedOut, locked);
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            if (await LockedMinutesAsync(user) is { } nowLocked) throw new InvalidCredentialsException(ErrorCode.AuthLockedOut, nowLocked);
            return null;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return user.Id;
    }

    public async Task<Guid?> ValidateAdminAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || await userManager.IsLockedOutAsync(user)) return null;

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return null;
        }

        if (!await userManager.IsInRoleAsync(user, AuthClaims.AdminRole)) return null;

        await userManager.ResetAccessFailedCountAsync(user);
        return user.Id;
    }

    public async Task<string> GetSessionStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        if (string.IsNullOrEmpty(user.SecurityStamp)) await userManager.UpdateSecurityStampAsync(user);
        return SessionStamps.Fingerprint(user.SecurityStamp);
    }

    public async Task<PasswordState> GetPasswordStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        return new PasswordState(user.PasswordChangedAt, user.PasswordChangedAt + PasswordPolicy.ChangeInterval);
    }

    public async Task<string> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        var now = clock.GetUtcNow();

        if (user.PasswordChangedAt + PasswordPolicy.ChangeInterval is { } next && next > now)
            throw new ConflictException(ErrorCode.AuthPasswordChangeTooSoon, LocalTime(next));

        // Con la cuenta bloqueada no se comprueba nada: un 409 y no un 401, que el frontal tomaría por sesión caducada.
        if (await LockedMinutesAsync(user) is { } locked) throw new ConflictException(ErrorCode.AuthLockedOut, locked);
        if (!await userManager.CheckPasswordAsync(user, currentPassword))
        {
            await userManager.AccessFailedAsync(user);
            if (await LockedMinutesAsync(user) is { } nowLocked) throw new ConflictException(ErrorCode.AuthLockedOut, nowLocked);
            throw Invalid("currentPassword", ErrorCode.AuthCurrentPasswordWrong);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw Invalid("newPassword", ErrorCode.AuthPasswordSame);

        // ChangePasswordAsync guarda el usuario (con la contraseña nueva, la SecurityStamp nueva y la fecha del cambio)
        // solo si la contraseña cumple las reglas; si no, la fecha se deshace para que no la guarde otro SaveChanges.
        var previous = user.PasswordChangedAt;
        user.PasswordChangedAt = now;
        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            user.PasswordChangedAt = previous;
            throw Failures(result, passwordField: "newPassword");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        sessions.Forget(user.Id);
        return user.Email!;
    }

    private async Task<ApplicationUser> RequireAsync(Guid userId) =>
        await userManager.FindByIdAsync(userId.ToString()) ?? throw new UnauthenticatedException();

    private async Task<int?> LockedMinutesAsync(ApplicationUser user)
    {
        if (!await userManager.IsLockedOutAsync(user)) return null;
        var end = await userManager.GetLockoutEndDateAsync(user) ?? clock.GetUtcNow();
        return Math.Max(1, (int)Math.Ceiling((end - clock.GetUtcNow()).TotalMinutes));
    }

    private static string LocalTime(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, SiteZone).ToString("d 'de' MMMM 'a las' HH:mm", CultureInfo.GetCultureInfo("es-ES"));

    private static ValidationException Invalid(string field, string code) =>
        new([new ValidationFailure(field, ErrorCatalog.Format(code)) { ErrorCode = code, CustomState = Array.Empty<object>() }]);

    // Cada regla de Identity tiene su código en el catálogo (identity.PasswordTooShort...) para poder traducirla.
    private ValidationException Failures(IdentityResult result, string passwordField)
    {
        var failures = result.Errors.Select(e =>
        {
            var field = e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? passwordField : "email";
            var code = "identity." + e.Code;
            object[] args = e.Code switch
            {
                "PasswordTooShort" => [userManager.Options.Password.RequiredLength],
                "PasswordRequiresUniqueChars" => [userManager.Options.Password.RequiredUniqueChars],
                "PasswordTooLong" => [PasswordPolicy.MaxLength],
                "PasswordRepeatedChars" => [PasswordPolicy.MaxRepeatedChars],
                _ => [],
            };
            return ErrorCatalog.Contains(code)
                ? new ValidationFailure(field, ErrorCatalog.Format(code, args)) { ErrorCode = code, CustomState = args }
                : new ValidationFailure(field, e.Description);
        });

        return new ValidationException(failures);
    }
}
