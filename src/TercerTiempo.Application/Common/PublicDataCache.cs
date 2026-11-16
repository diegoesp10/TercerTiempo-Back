using System.Collections.Concurrent;

namespace TercerTiempo.Application.Common;

// Lecturas públicas que piden muchas cuentas a la vez y solo cambian con los datos reales (ligas, clasificaciones,
// jornadas, catálogo de clubes, árbitros): se guardan en memoria por clave (temporada, liga, categoría…) con una versión
// de los datos públicos. Cada copia de la base real, cada sincronización y cada guardado que toque esas tablas sube la
// versión (Invalidate) y lo guardado deja de valer. Lo guardado tiene que ser de solo lectura (DTO o entidades sin
// seguimiento que nadie modifica).
public sealed class PublicDataCache(TimeProvider clock)
{
    // Por si algo cambia sin pasar por Invalidate (una corrección a mano en la base): como mucho este tiempo.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public async Task<T> GetAsync<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
        where T : class
    {
        var version = Version;
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(key, out var entry) && entry.Version == version && entry.ExpiresAt > now)
            return (T)entry.Value;

        var value = await load(cancellationToken);
        // Si los datos han cambiado mientras se leían, no se guarda lo leído: la siguiente lectura ya trae lo nuevo.
        if (Version == version)
            _entries[key] = new Entry(value, version, now + Lifetime);
        return value;
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _entries.Clear();
    }

    private sealed record Entry(object Value, long Version, DateTimeOffset ExpiresAt);
}
