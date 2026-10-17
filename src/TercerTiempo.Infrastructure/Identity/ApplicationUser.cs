using Microsoft.AspNetCore.Identity;

namespace TercerTiempo.Infrastructure.Identity;

/// <summary>Usuario de ASP.NET Core Identity. El perfil de jugador vive en el dominio (Player.UserId).</summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
}
