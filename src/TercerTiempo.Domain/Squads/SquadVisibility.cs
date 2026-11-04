namespace TercerTiempo.Domain.Squads;

public enum SquadVisibility
{
    // Nadie de fuera puede pedir el acceso; el club sigue pudiendo dar pases.
    Private = 0,

    // Otras cuentas pueden solicitar el acceso al acta interna y al vídeo de cada partido; nada se ve sin un pase.
    Public = 1,
}
