namespace TercerTiempo.Domain.Squads;

/// <summary>Quién puede ver los datos de rendimiento de una plantilla.</summary>
public enum SquadVisibility
{
    /// <summary>Solo el cuerpo técnico y los jugadores vinculados a la plantilla (valor por defecto).</summary>
    Private = 0,

    /// <summary>Cualquier usuario puede consultar las actas y estadísticas de la plantilla.</summary>
    Public = 1,
}
