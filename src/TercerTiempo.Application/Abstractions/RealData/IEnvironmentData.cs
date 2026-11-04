namespace TercerTiempo.Application.Abstractions.RealData;

// Las descargas corren contra la base de datos reales, donde no está lo de la app (clubes, plantillas, actas internas).
// Lo poco del entorno que descarga que necesitan se lee por aquí, siempre de la base de este entorno.
public interface IEnvironmentData
{
    // Equipos que tienen plantilla en la app en esa temporada: sus actas se piden a la federación.
    Task<IReadOnlySet<Guid>> TeamsWithSquadAsync(Guid seasonId, CancellationToken cancellationToken);
}
