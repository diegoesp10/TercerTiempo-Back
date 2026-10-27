using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Infrastructure.Identity;

// TOTP de Identity (RFC 6238: 6 cifras cada 30 s, lo que generan Microsoft Authenticator y compañía). La clave vive en
// AspNetUserTokens; TwoFactorEnabled marca que la app ya está dada de alta (con un primer código bueno).
internal sealed class AdminSecondFactor(
    UserManager<ApplicationUser> users,
    SessionStamps sessions,
    IDataProtectionProvider protection,
    IOptions<AdminOptions> options,
    TimeProvider clock) : IAdminSecondFactor
{
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    private const string Issuer = "Tercer Tiempo";
    private const string TokenProvider = "TercerTiempo";
    private const string LastCodeToken = "AdminMfaLastCode";

    // Identity acepta el código de hasta 2 ventanas antes o después (±60-90 s): uno ya usado no vale en ese margen.
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(3);

    private ITimeLimitedDataProtector Tickets => protection.CreateProtector("TercerTiempo.AdminMfa.Ticket").ToTimeLimitedDataProtector();

    public bool Required => options.Value.RequireMfa;

    public async Task<AdminMfaChallenge> ChallengeAsync(Guid userId, string userName, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthenticatedException();

        AdminMfaSetup? setup = null;
        if (!user.TwoFactorEnabled)
        {
            // Mientras no se confirme con un código, cada login enseña la misma clave (por si el primer intento falló).
            var key = await users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await users.ResetAuthenticatorKeyAsync(user);
                sessions.Forget(user.Id);
                key = await users.GetAuthenticatorKeyAsync(user);
            }
            setup = new AdminMfaSetup(GroupKey(key!), AuthenticatorUri(user.UserName ?? userName, key!));
        }

        // El billete lleva la SecurityStamp: si cambia (contraseña, reinicio del MFA) deja de valer.
        var ticket = Tickets.Protect($"{user.Id:N}|{user.SecurityStamp}", TicketLifetime);
        return new AdminMfaChallenge(ticket, clock.GetUtcNow() + TicketLifetime, setup);
    }

    public async Task<VerifiedAdmin> VerifyAsync(string ticket, string code, CancellationToken cancellationToken)
    {
        if (!Required) throw new ConflictException(ErrorCode.AdminMfaExpired);

        string payload;
        try
        {
            payload = Tickets.Unprotect(ticket, out _);
        }
        catch (CryptographicException)
        {
            throw new ConflictException(ErrorCode.AdminMfaExpired);
        }

        var parts = payload.Split('|', 2);
        var user = parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var userId) ? await users.FindByIdAsync(userId.ToString()) : null;
        if (user is null || user.SecurityStamp != parts[1] || !await users.IsInRoleAsync(user, AuthClaims.AdminRole))
            throw new ConflictException(ErrorCode.AdminMfaExpired);

        if (await LockedMinutesAsync(user) is { } locked) throw new ConflictException(ErrorCode.AuthLockedOut, locked);

        var digits = new string(code.Where(char.IsAsciiDigit).ToArray());
        var now = clock.GetUtcNow();
        if (digits.Length != 6 || await WasUsedAsync(user, digits, now)
            || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, digits))
        {
            await users.AccessFailedAsync(user);
            if (await LockedMinutesAsync(user) is { } nowLocked) throw new ConflictException(ErrorCode.AuthLockedOut, nowLocked);
            throw new ValidationException([new ValidationFailure("code", ErrorCatalog.Format(ErrorCode.AdminMfaInvalidCode))
            {
                ErrorCode = ErrorCode.AdminMfaInvalidCode,
                CustomState = Array.Empty<object>(),
            }]);
        }

        await users.SetAuthenticationTokenAsync(user, TokenProvider, LastCodeToken, $"{digits}:{now.ToUnixTimeSeconds()}");
        if (!user.TwoFactorEnabled)
        {
            await users.SetTwoFactorEnabledAsync(user, true);
            sessions.Forget(user.Id);
        }
        await users.ResetAccessFailedCountAsync(user);
        return new VerifiedAdmin(user.Id, user.UserName!);
    }

    private async Task<bool> WasUsedAsync(ApplicationUser user, string digits, DateTimeOffset now)
    {
        var last = await users.GetAuthenticationTokenAsync(user, TokenProvider, LastCodeToken);
        if (last?.Split(':') is not [var usedCode, var usedAt] || !long.TryParse(usedAt, out var seconds)) return false;
        return usedCode == digits && now - DateTimeOffset.FromUnixTimeSeconds(seconds) < ReplayWindow;
    }

    private async Task<int?> LockedMinutesAsync(ApplicationUser user)
    {
        if (!await users.IsLockedOutAsync(user)) return null;
        var end = await users.GetLockoutEndDateAsync(user) ?? clock.GetUtcNow();
        return Math.Max(1, (int)Math.Ceiling((end - clock.GetUtcNow()).TotalMinutes));
    }

    private static string GroupKey(string key) =>
        string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(chunk => new string(chunk)));

    internal static string AuthenticatorUri(string userName, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(userName)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
}
