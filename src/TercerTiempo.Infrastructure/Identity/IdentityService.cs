using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
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

        if (!result.Succeeded)
        {
            // Cada regla de Identity tiene su código en el catálogo (identity.PasswordTooShort...) para poder traducirla.
            var failures = result.Errors.Select(e =>
            {
                var field = e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "password" : "email";
                var code = "identity." + e.Code;
                object[] args = e.Code switch
                {
                    "PasswordTooShort" => [userManager.Options.Password.RequiredLength],
                    "PasswordRequiresUniqueChars" => [userManager.Options.Password.RequiredUniqueChars],
                    _ => [],
                };
                return ErrorCatalog.Contains(code)
                    ? new ValidationFailure(field, ErrorCatalog.Format(code, args)) { ErrorCode = code, CustomState = args }
                    : new ValidationFailure(field, e.Description);
            });

            throw new ValidationException(failures);
        }

        return user.Id;
    }

    public async Task<Guid?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return null;

        return await userManager.CheckPasswordAsync(user, password) ? user.Id : null;
    }
}
