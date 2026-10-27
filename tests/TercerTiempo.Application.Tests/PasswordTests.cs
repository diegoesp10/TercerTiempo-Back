using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Auth.ChangePassword;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Infrastructure.Identity;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Application.Tests;

public class PasswordTests
{
    private const string Strong = "Lince-Verde-47";

    [Theory]
    [InlineData("Futbol2026!")]
    [InlineData("Sporting1905!")]
    [InlineData("P4ssw0rd!2026")]
    [InlineData("Qwerty!2026Ab")]
    [InlineData("Ab1!123456Xy")]
    [InlineData("Contraseña#2026")]
    [InlineData("Xbbbb1!kdefgh")]
    [InlineData("Pelayo.Ruiz-99")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        var user = new ApplicationUser { Email = "pelayo.ruiz@example.com", UserName = "pelayo.ruiz@example.com" };
        var result = await new PasswordRulesValidator().ValidateAsync(null!, user, password);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(Strong)]
    [InlineData("Nube#Cobre83Ri")]
    [InlineData("tRen.de.Vigo_2031")]
    public async Task Hard_to_guess_passwords_pass_the_extra_rules(string password)
    {
        var user = new ApplicationUser { Email = "pelayo.ruiz@example.com", UserName = "pelayo.ruiz@example.com" };
        var result = await new PasswordRulesValidator().ValidateAsync(null!, user, password);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Code)));
    }

    [Fact]
    public void The_new_password_must_be_long_and_different()
    {
        var validator = new ChangePasswordValidator();
        var same = validator.Validate(new ChangePasswordCommand(Strong, Strong));
        Assert.Contains(same.Errors, e => e.ErrorCode == ErrorCode.AuthPasswordSame);
        var shortOne = validator.Validate(new ChangePasswordCommand(Strong, "Ab1!x"));
        Assert.Contains(shortOne.Errors, e => e.ErrorCode == ErrorCode.AuthPasswordTooShort);
        Assert.True(validator.Validate(new ChangePasswordCommand(Strong, "Nube#Cobre83Ri")).IsValid);
    }

    [LocalSqlFact]
    public async Task Changing_the_password_is_checked_limited_to_once_a_day_and_closes_other_sessions()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var identity = fixture.Identity;
        var userId = await identity.CreateUserAsync("pelayo.ruiz@example.com", Strong, default);
        var oldStamp = await identity.GetSessionStampAsync(userId, default);
        Assert.True(await fixture.Sessions.IsCurrentAsync(Principal(userId, oldStamp), default));

        var wrong = await Assert.ThrowsAsync<ValidationException>(() => identity.ChangePasswordAsync(userId, "Otra-Clave-55", "Nube#Cobre83Ri", default));
        Assert.Contains(wrong.Errors, e => e.ErrorCode == ErrorCode.AuthCurrentPasswordWrong && e.PropertyName == "currentPassword");

        var weak = await Assert.ThrowsAsync<ValidationException>(() => identity.ChangePasswordAsync(userId, Strong, "futbol2026", default));
        Assert.Contains(weak.Errors, e => e.ErrorCode == ErrorCode.IdentityPasswordRequiresUpper);
        Assert.Contains(weak.Errors, e => e.ErrorCode == ErrorCode.IdentityPasswordTooCommon);
        Assert.Null((await identity.GetPasswordStateAsync(userId, default)).ChangedAt);

        Assert.Equal("pelayo.ruiz@example.com", await identity.ChangePasswordAsync(userId, Strong, "Nube#Cobre83Ri", default));
        var state = await identity.GetPasswordStateAsync(userId, default);
        Assert.Equal(fixture.Clock.GetUtcNow(), state.ChangedAt);
        Assert.Equal(fixture.Clock.GetUtcNow().AddHours(24), state.NextChangeAt);

        // El token de antes del cambio ya no vale; uno nuevo sí.
        Assert.False(await fixture.Sessions.IsCurrentAsync(Principal(userId, oldStamp), default));
        var newStamp = await identity.GetSessionStampAsync(userId, default);
        Assert.True(await fixture.Sessions.IsCurrentAsync(Principal(userId, newStamp), default));
        Assert.Equal(userId, await identity.ValidateCredentialsAsync("pelayo.ruiz@example.com", "Nube#Cobre83Ri", default));
        Assert.Null(await identity.ValidateCredentialsAsync("pelayo.ruiz@example.com", Strong, default));

        fixture.Clock.Advance(TimeSpan.FromHours(23));
        var tooSoon = await Assert.ThrowsAsync<ConflictException>(() => identity.ChangePasswordAsync(userId, "Nube#Cobre83Ri", "tRen.de.Vigo_2031", default));
        Assert.Equal(ErrorCode.AuthPasswordChangeTooSoon, tooSoon.Code);

        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await identity.ChangePasswordAsync(userId, "Nube#Cobre83Ri", "tRen.de.Vigo_2031", default);
    }

    [LocalSqlFact]
    public async Task Five_failed_logins_lock_the_account_for_a_while()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var identity = fixture.Identity;
        await identity.CreateUserAsync("bloqueo@example.com", Strong, default);

        for (var i = 0; i < PasswordPolicy.MaxFailedAttempts - 1; i++)
            Assert.Null(await identity.ValidateCredentialsAsync("bloqueo@example.com", "Mala-Clave-" + i, default));

        var locked = await Assert.ThrowsAsync<InvalidCredentialsException>(() => identity.ValidateCredentialsAsync("bloqueo@example.com", "Mala-Clave-9", default));
        Assert.Equal(ErrorCode.AuthLockedOut, locked.Code);
        var stillLocked = await Assert.ThrowsAsync<InvalidCredentialsException>(() => identity.ValidateCredentialsAsync("bloqueo@example.com", Strong, default));
        Assert.Equal(ErrorCode.AuthLockedOut, stillLocked.Code);
    }

    private static ClaimsPrincipal Principal(Guid userId, string stamp) => new(new ClaimsIdentity(
    [
        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new Claim(AuthClaims.SessionStamp, stamp),
    ], "test"));

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    // Identity de verdad (mismas reglas que la API) sobre una base temporal que se borra al terminar.
    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private IdentityFixture(ServiceProvider provider)
        {
            _provider = provider;
            _scope = provider.CreateAsyncScope();
            Clock = (ManualClock)provider.GetRequiredService<TimeProvider>();
            Sessions = provider.GetRequiredService<SessionStamps>();
            Identity = new IdentityService(_scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), Sessions, Clock);
        }

        public ManualClock Clock { get; }
        public SessionStamps Sessions { get; }
        public IdentityService Identity { get; }

        public static async Task<IdentityFixture> CreateAsync()
        {
            var database = $"TercerTiempo_PasswordVerification_{Guid.NewGuid():N}";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero)));
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
                $"Server=localhost;Database={database};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10"));
            services.AddIdentityCore<ApplicationUser>(IdentitySetup.Configure)
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddPasswordValidator<PasswordRulesValidator>();
            services.Configure<PasswordHasherOptions>(IdentitySetup.ConfigureHasher);
            services.AddSingleton<SessionStamps>();

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
