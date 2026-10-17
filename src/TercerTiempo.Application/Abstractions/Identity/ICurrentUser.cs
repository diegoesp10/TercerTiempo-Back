namespace TercerTiempo.Application.Abstractions.Identity;

/// <summary>Usuario que ejecuta la petición actual (lo resuelve la API a partir del JWT).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? PlayerId { get; }
}
