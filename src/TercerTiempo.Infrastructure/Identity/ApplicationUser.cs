using Microsoft.AspNetCore.Identity;

namespace TercerTiempo.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    // Último cambio de contraseña hecho por la cuenta (no cuenta la del alta): limita los cambios a uno al día.
    public DateTimeOffset? PasswordChangedAt { get; set; }
}
