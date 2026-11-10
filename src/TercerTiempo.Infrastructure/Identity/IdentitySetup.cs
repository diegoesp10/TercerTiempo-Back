using Microsoft.AspNetCore.Identity;
using TercerTiempo.Application.Auth;

namespace TercerTiempo.Infrastructure.Identity;

// Bloqueo de la cuenta tras PasswordPolicy.MaxFailedAttempts intentos fallidos (login, segundo paso del administrador y
// cambio de contraseña). En publicación siempre; en desarrollo y datos falsos se apaga (Lockout:Enabled) para poder
// probar el login sin quedarse fuera 15 minutos.
public sealed class AccountLockoutOptions
{
    public const string SectionName = "Lockout";

    public bool Enabled { get; set; } = true;
}

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
