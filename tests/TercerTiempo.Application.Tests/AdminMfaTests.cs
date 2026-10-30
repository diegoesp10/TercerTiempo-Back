using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Admin;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Infrastructure.Identity;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

// Segundo paso (TOTP de Microsoft Authenticator) del login del administrador, solo donde Admin:RequireMfa lo pide.
public class AdminMfaTests
{
    private const string Strong = "Lince-Verde-47";

    [Fact]
    public async Task Without_mfa_the_login_returns_the_token_and_with_it_only_the_challenge()
    {
        var adminId = Guid.NewGuid();
        bool? pending = null;
        Task<Guid?> ValidateAdmin(bool secondFactorPending)
        {
            pending = secondFactorPending;
            return Task.FromResult<Guid?>(adminId);
        }
        var identity = Stub<IIdentityService>((name, args) => name switch
        {
            nameof(IIdentityService.ValidateAdminAsync) => ValidateAdmin((bool)args![2]!),
            nameof(IIdentityService.GetSessionStampAsync) => Task.FromResult("huella"),
            _ => throw new NotSupportedException(name),
        });
        var tokens = Stub<ITokenService>((name, _) => new AuthToken("jwt", DateTimeOffset.UnixEpoch));
        var challenge = new AdminMfaChallenge("billete", DateTimeOffset.UnixEpoch, null);

        var plain = await new AdminLoginHandler(identity, tokens, new FakeSecondFactor(false, challenge)).Handle(new AdminLoginCommand(" diego ", "x"), default);
        Assert.Equal(("jwt", "diego", (AdminMfaChallenge?)null), (plain.AccessToken, plain.UserName, plain.Mfa));
        Assert.False(pending);

        var withMfa = await new AdminLoginHandler(identity, tokens, new FakeSecondFactor(true, challenge)).Handle(new AdminLoginCommand("diego", "x"), default);
        Assert.Null(withMfa.AccessToken);
        Assert.Same(challenge, withMfa.Mfa);
        Assert.True(pending);

        var verified = await new AdminVerifyMfaHandler(identity, tokens, new FakeSecondFactor(true, challenge, new VerifiedAdmin(adminId, "diego")))
            .Handle(new AdminVerifyMfaCommand("billete", "123456"), default);
        Assert.Equal(("jwt", "diego"), (verified.AccessToken, verified.UserName));
    }

    [LocalSqlFact]
    public async Task The_app_is_enrolled_with_the_first_good_code_and_codes_cannot_be_reused_or_guessed()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var (mfa, users) = (fixture.SecondFactor, fixture.Users);
        var admin = await fixture.CreateAdminAsync("diego");

        // Primera vez: la clave y el QR; mientras no se confirme, siempre la misma.
        var first = await mfa.ChallengeAsync(admin.Id, "diego", default);
        Assert.NotNull(first.Setup);
        var key = first.Setup!.SharedKey.Replace(" ", "").ToUpperInvariant();
        Assert.StartsWith("otpauth://totp/Tercer%20Tiempo:diego?secret=" + key, first.Setup.AuthenticatorUri);
        var again = await mfa.ChallengeAsync(admin.Id, "diego", default);
        Assert.Equal(first.Setup.SharedKey, again.Setup!.SharedKey);

        var wrongCode = Totp(key, offset: 10);
        var wrong = await Assert.ThrowsAsync<ValidationException>(() => mfa.VerifyAsync(again.Ticket, wrongCode, default));
        Assert.Contains(wrong.Errors, e => e.ErrorCode == ErrorCode.AdminMfaInvalidCode && e.PropertyName == "code");
        Assert.False((await users.FindByIdAsync(admin.Id.ToString()))!.TwoFactorEnabled);

        var code = Totp(key);
        Assert.Equal(new VerifiedAdmin(admin.Id, "diego"), await mfa.VerifyAsync(again.Ticket, code[..3] + " " + code[3..], default));
        Assert.True((await users.FindByIdAsync(admin.Id.ToString()))!.TwoFactorEnabled);
        Assert.Equal(0, await users.GetAccessFailedCountAsync(admin));

        // Dada de alta: ya no se enseña la clave, y el mismo código no vale dos veces.
        var next = await mfa.ChallengeAsync(admin.Id, "diego", default);
        Assert.Null(next.Setup);
        await Assert.ThrowsAsync<ValidationException>(() => mfa.VerifyAsync(next.Ticket, code, default));
        Assert.Equal(new VerifiedAdmin(admin.Id, "diego"), await mfa.VerifyAsync(next.Ticket, Totp(key, offset: 1), default));

        var tampered = await Assert.ThrowsAsync<ConflictException>(() => mfa.VerifyAsync(next.Ticket[..^4] + "AAAA", Totp(key, offset: -1), default));
        Assert.Equal(ErrorCode.AdminMfaExpired, tampered.Code);
    }

    [LocalSqlFact]
    public async Task Wrong_codes_lock_the_admin_even_if_the_password_is_entered_again()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var admin = await fixture.CreateAdminAsync("diego");
        var key = (await fixture.SecondFactor.ChallengeAsync(admin.Id, "diego", default)).Setup!.SharedKey.Replace(" ", "").ToUpperInvariant();

        for (var i = 0; i < PasswordPolicy.MaxFailedAttempts - 1; i++)
        {
            // La contraseña buena con el segundo paso pendiente no borra los fallos de código.
            Assert.Equal(admin.Id, await fixture.Identity.ValidateAdminAsync("diego", Strong, secondFactorPending: true, default));
            var ticket = (await fixture.SecondFactor.ChallengeAsync(admin.Id, "diego", default)).Ticket;
            await Assert.ThrowsAsync<ValidationException>(() => fixture.SecondFactor.VerifyAsync(ticket, Totp(key, offset: 10 + i), default));
        }

        var last = (await fixture.SecondFactor.ChallengeAsync(admin.Id, "diego", default)).Ticket;
        var locked = await Assert.ThrowsAsync<ConflictException>(() => fixture.SecondFactor.VerifyAsync(last, "000000", default));
        Assert.Equal(ErrorCode.AuthLockedOut, locked.Code);
        var stillLocked = await Assert.ThrowsAsync<ConflictException>(() => fixture.SecondFactor.VerifyAsync(last, Totp(key), default));
        Assert.Equal(ErrorCode.AuthLockedOut, stillLocked.Code);
    }

    [LocalSqlFact]
    public async Task With_mfa_a_new_database_gets_the_admin_even_with_a_password_that_breaks_the_rules()
    {
        const string Weak = "futbol2026";
        await using (var withMfa = await IdentityFixture.CreateAsync(o => (o.UserName, o.Password, o.RequireMfa) = ("diego", Weak, true)))
        {
            await withMfa.Provider.SeedAdminAsync();
            Assert.NotNull(await withMfa.Identity.ValidateAdminAsync("diego", Weak, secondFactorPending: true, default));
        }

        await using var withoutMfa = await IdentityFixture.CreateAsync(o => (o.UserName, o.Password, o.RequireMfa) = ("diego", Weak, false));
        await withoutMfa.Provider.SeedAdminAsync();
        Assert.Null(await withoutMfa.Users.FindByNameAsync("diego"));
    }

    // RFC 6238 con los valores de Microsoft Authenticator: HMAC-SHA1, 30 s, 6 cifras.
    private static string Totp(string base32Key, int offset = 0)
    {
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + offset;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(FromBase32(base32Key), counter);
        var start = hash[^1] & 0x0f;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(start)) & 0x7fffffff) % 1_000_000;
        return value.ToString("D6");
    }

    private static byte[] FromBase32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }

    private sealed class FakeSecondFactor(bool required, AdminMfaChallenge challenge, VerifiedAdmin? verified = null) : IAdminSecondFactor
    {
        public bool Required => required;
        public Task<AdminMfaChallenge> ChallengeAsync(Guid userId, string userName, CancellationToken cancellationToken) => Task.FromResult(challenge);
        public Task<VerifiedAdmin> VerifyAsync(string ticket, string code, CancellationToken cancellationToken) => Task.FromResult(verified!);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }

    // Identity de verdad (mismas reglas que la API) sobre una base temporal que se borra al terminar.
    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private IdentityFixture(ServiceProvider provider)
        {
            _provider = provider;
            Provider = provider;
            _scope = provider.CreateAsyncScope();
            Users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Identity = new IdentityService(Users, provider.GetRequiredService<SessionStamps>(), TimeProvider.System, provider.GetRequiredService<ILogger<IdentityService>>());
            SecondFactor = _scope.ServiceProvider.GetRequiredService<AdminSecondFactor>();
        }

        public IServiceProvider Provider { get; }
        public UserManager<ApplicationUser> Users { get; }
        public IdentityService Identity { get; }
        public AdminSecondFactor SecondFactor { get; }

        public async Task<ApplicationUser> CreateAdminAsync(string userName)
        {
            var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            await roles.CreateAsync(new IdentityRole<Guid>(AuthClaims.AdminRole) { Id = Guid.CreateVersion7() });
            var user = new ApplicationUser { Id = Guid.CreateVersion7(), UserName = userName, Email = $"{userName}@example.com", EmailConfirmed = true };
            Assert.True((await Users.CreateAsync(user, Strong)).Succeeded);
            Assert.True((await Users.AddToRoleAsync(user, AuthClaims.AdminRole)).Succeeded);
            return user;
        }

        public static async Task<IdentityFixture> CreateAsync(Action<AdminOptions>? admin = null)
        {
            var database = $"TercerTiempo_AdminMfaVerification_{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
                $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10"));
            services.AddIdentityCore<ApplicationUser>(IdentitySetup.Configure)
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddPasswordValidator<PasswordRulesValidator>()
                .AddTokenProvider<AuthenticatorTokenProvider<ApplicationUser>>(TokenOptions.DefaultAuthenticatorProvider);
            services.AddSingleton<SessionStamps>();
            services.AddDataProtection();
            services.Configure<AdminOptions>(o =>
            {
                o.RequireMfa = true;
                admin?.Invoke(o);
            });
            services.AddScoped<AdminSecondFactor>();

            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            return new IdentityFixture(provider);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }
}
