namespace TercerTiempo.Application.Abstractions.Identity;

/// <summary>Tipo de cuenta: cada usuario es jugador, miembro del cuerpo técnico o club.</summary>
public enum AccountType
{
    Player = 1,
    Coach = 2,

    /// <summary>Club: da de alta a los técnicos de sus equipos con códigos de acceso por temporada.</summary>
    Club = 3,
}
