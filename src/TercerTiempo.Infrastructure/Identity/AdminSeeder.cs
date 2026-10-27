using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Identity;

namespace TercerTiempo.Infrastructure.Identity;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string UserName { get; set; } = "diego";
    public string Email { get; set; } = "diego@tercertiempo.local";

    // Nunca en appsettings: user-secrets o variable de entorno Admin__Password.
    public string Password { get; set; } = string.Empty;
}

public static class AdminSeeder
{
    // Crea el rol Admin y el usuario administrador de la configuración. La contraseña configurada manda: si cambia,
    // se sustituye la guardada al arrancar.
    public static async Task SeedAdminAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder));

        if (string.IsNullOrWhiteSpace(settings.UserName) || string.IsNullOrEmpty(settings.Password))
        {
            logger.LogWarning("Sin administrador: configura Admin:UserName y Admin:Password (user-secrets) para entrar en /jobs");
            return;
        }

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(AuthClaims.AdminRole))
            Ensure(await roles.CreateAsync(new IdentityRole<Guid>(AuthClaims.AdminRole) { Id = Guid.CreateVersion7() }), "crear el rol Admin");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(settings.UserName.Trim());
        var candidate = user ?? new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = settings.UserName.Trim(),
            Email = settings.Email.Trim(),
            EmailConfirmed = true,
        };

        // La contraseña del administrador sigue las mismas reglas que las de las cuentas. Si la configurada no las
        // cumple, la API arranca igual (sin crear ni cambiar el administrador) y lo dice en el log.
        var weak = await PolicyProblemsAsync(users, candidate, settings.Password);
        if (weak.Count > 0)
        {
            var reasons = string.Join(" ", weak);
            if (user is null || !await users.CheckPasswordAsync(user, settings.Password))
            {
                logger.LogError("Admin:Password no cumple las reglas de contraseña; no se crea ni se cambia el administrador. {Reasons}", reasons);
                return;
            }
            logger.LogWarning("La contraseña del administrador ya no cumple las reglas de contraseña; cámbiala en Admin:Password. {Reasons}", reasons);
        }

        if (user is null)
        {
            user = candidate;
            Ensure(await users.CreateAsync(user, settings.Password), "crear el administrador");
            logger.LogInformation("Administrador {UserName} creado", user.UserName);
        }
        else if (!await users.CheckPasswordAsync(user, settings.Password))
        {
            Ensure(await users.RemovePasswordAsync(user), "quitar la contraseña anterior del administrador");
            Ensure(await users.AddPasswordAsync(user, settings.Password), "cambiar la contraseña del administrador");
            logger.LogInformation("Contraseña del administrador {UserName} actualizada desde la configuración", user.UserName);
        }

        if (!await users.IsInRoleAsync(user, AuthClaims.AdminRole))
            Ensure(await users.AddToRoleAsync(user, AuthClaims.AdminRole), "dar el rol Admin");
    }

    private static async Task<List<string>> PolicyProblemsAsync(UserManager<ApplicationUser> users, ApplicationUser user, string password)
    {
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            problems.AddRange(result.Errors.Select(e => e.Description));
        }
        return problems;
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"No se ha podido {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}
