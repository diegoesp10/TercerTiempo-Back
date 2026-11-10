namespace TercerTiempo.Application.Abstractions.RealData;

// Los datos reales (lo público de las webs y el estado de sus descargas) viven en una base compartida por todos los
// entornos; cada entorno tiene una copia. Lo que la administración cambia de ellos (ajustes de la pasada de las ligas,
// cola de actas, temporada en curso, escudos) se ejecuta contra la base compartida y después se pide la copia.
public interface IRealData
{
    // work recibe los servicios de un ámbito cuyo acceso a datos va a la base de datos reales.
    Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> work);

    // Trae enseguida a la base de este entorno lo que acaba de cambiar en la base de datos reales.
    void RequestCopy();
}
