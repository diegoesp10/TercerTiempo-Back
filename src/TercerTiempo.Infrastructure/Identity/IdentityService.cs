using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;

namespace TercerTiempo.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<Guid> CreateUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
            throw new ConflictException("Ya existe una cuenta con ese email.");

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var failures = result.Errors.Select(e => new ValidationFailure(
                e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "password" : "email",
                e.Description));

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
