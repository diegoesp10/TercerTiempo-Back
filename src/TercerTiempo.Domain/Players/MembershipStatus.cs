namespace TercerTiempo.Domain.Players;

public enum MembershipStatus
{
    /// <summary>Se ha unido al equipo pero no aparece en la plantilla del proveedor: pendiente de validar.</summary>
    Pending = 0,
    /// <summary>Miembro verificado de la plantilla: puede registrar rendimiento.</summary>
    Active = 1,
    /// <summary>Ha abandonado el equipo.</summary>
    Inactive = 2,
}
