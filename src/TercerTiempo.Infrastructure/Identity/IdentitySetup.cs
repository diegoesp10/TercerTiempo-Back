using Microsoft.AspNetCore.Identity;
using TercerTiempo.Application.Auth;

namespace TercerTiempo.Infrastructure.Identity;

internal static class IdentitySetup
{
    public static void Configure(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = PasswordPolicy.MinLength;
        options.Password.RequiredUniqueChars = PasswordPolicy.MinUniqueChars;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.MaxFailedAccessAttempts = PasswordPolicy.MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = PasswordPolicy.LockoutDuration;
        options.Lockout.AllowedForNewUsers = true;
    }

    // PBKDF2-HMAC-SHA512 con las iteraciones que recomienda OWASP; los hashes anteriores se rehacen al entrar.
    public static void ConfigureHasher(PasswordHasherOptions options) => options.IterationCount = 210_000;
}
