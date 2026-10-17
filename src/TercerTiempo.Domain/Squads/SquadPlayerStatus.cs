namespace TercerTiempo.Domain.Squads;

public enum SquadPlayerStatus
{
    Active = 1,

    /// <summary>Ha causado baja en la plantilla; se conserva por su historial de actas.</summary>
    Left = 2,
}
