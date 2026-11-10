using Microsoft.AspNetCore.Identity;

namespace TercerTiempo.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    // Último cambio de contraseña hecho por la cuenta (no cuenta la del alta): limita los cambios a uno al día.
    public DateTimeOffset? PasswordChangedAt { get; set; }

    // Cuenta confirmada: el alta terminó y su perfil (club, técnico o jugador) quedó guardado con ella. Sin confirmar no
    // entra ni ocupa el email: es un alta que se cortó a medias y la siguiente con ese email la sustituye.
    public DateTimeOffset? ConfirmedAt { get; set; }
}
