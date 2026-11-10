using Microsoft.Extensions.DependencyInjection;

namespace TercerTiempo.Infrastructure.RealData;

// A qué base va el AppDbContext de un ámbito: a la del entorno o a la de los datos reales. Las descargas (los trabajos en
// segundo plano que leen las webs y lo que la administración cambia de los datos reales) corren dentro de Enter(): todo
// ámbito que se abra en ese flujo escribe en la base compartida, también los del registro de llamadas (ScrapeRequests) y
// del control de peticiones de la federación, sin que el código de las descargas tenga que saberlo.
internal static class RealDataAmbient
{
    private static readonly AsyncLocal<bool> Current = new();

    public static bool Active => Current.Value;

    public static IDisposable Enter() => Set(true);

    // Un ámbito que va a la base real aunque quien lo abre no esté en un flujo de descargas.
    public static IServiceScope CreateRealDataScope(this IServiceScopeFactory scopes) => CreateScope(scopes, true);

    // Y uno que va a la base del entorno aunque se abra dentro de una descarga.
    public static IServiceScope CreateEnvironmentScope(this IServiceScopeFactory scopes) => CreateScope(scopes, false);

    private static IServiceScope CreateScope(IServiceScopeFactory scopes, bool realData)
    {
        using (Set(realData))
        {
            var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<DatabaseTarget>();
            return scope;
        }
    }

    private static IDisposable Set(bool realData)
    {
        var previous = Current.Value;
        Current.Value = realData;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

// Se fija al crearse, la primera vez que el ámbito pide su AppDbContext.
internal sealed class DatabaseTarget
{
    public bool RealData { get; } = RealDataAmbient.Active;
}

internal sealed class RealDataOptions
{
    public const string SectionName = "RealData";

    // Este entorno puede descargar de las webs cuando ningún otro lo está haciendo (ver RealDataLease).
    public bool Ingest { get; set; }

    // Minutos entre copias de la base real a la de este entorno.
    public int CopyMinutes { get; set; } = 5;

    // Solo para crearla: si la base real está vacía, al arrancar se llena con los datos reales de la base de este entorno.
    public bool SeedFromThisEnvironment { get; set; }
}

internal sealed record RealDataConnections(string Environment, string RealData);
