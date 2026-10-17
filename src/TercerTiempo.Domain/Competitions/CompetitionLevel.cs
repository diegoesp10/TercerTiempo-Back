namespace TercerTiempo.Domain.Competitions;

/// <summary>Nivel de la pirámide: sirve para agrupar y ordenar las ligas en la interfaz.</summary>
public enum CompetitionLevel
{
    /// <summary>Primera y Segunda División.</summary>
    Professional = 1,
    /// <summary>1ª, 2ª y 3ª RFEF.</summary>
    National = 2,
    /// <summary>1ª, 2ª y 3ª AsturFútbol (antiguas Preferente, Primera y Segunda Regional).</summary>
    Regional = 3,
    Cup = 4,
    Youth = 5,
    Other = 6,
}
