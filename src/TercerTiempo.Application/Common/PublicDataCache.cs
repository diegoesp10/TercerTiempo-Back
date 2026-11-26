using System.Collections.Concurrent;

namespace TercerTiempo.Application.Common;

// Lecturas públicas que piden muchas cuentas a la vez y solo cambian con los datos reales (ligas, clasificaciones,
// jornadas, catálogo de clubes, árbitros): se guardan en memoria por clave (temporada, liga, categoría…). Cada lectura
// dice de qué datos depende (PublicTags) y solo deja de valer cuando cambian esos: la nota de un árbitro no tira la
// clasificación, ni la clasificación de una temporada la de otra. La copia de la base real y la sincronización de las
// ligas lo tiran todo (Invalidate()). Lo guardado tiene que ser de solo lectura (DTO o entidades sin seguimiento que
// nadie modifica).
public sealed class PublicDataCache(TimeProvider clock)
{
    // Por si algo cambia sin pasar por Invalidate (una corrección a mano en la base): como mucho este tiempo.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, long> _tags = new();
    private long _version;

    public async Task<T> GetAsync<T>(string key, IReadOnlyList<string> tags, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
        where T : class
    {
        var versions = Versions(tags);
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > now && entry.Versions.AsSpan().SequenceEqual(versions))
            return (T)entry.Value;

        var value = await load(cancellationToken);
        // Si sus datos han cambiado mientras se leían, no se guarda lo leído: la siguiente lectura ya trae lo nuevo.
        if (Versions(tags).AsSpan().SequenceEqual(versions))
            _entries[key] = new Entry(value, versions, now + Lifetime);
        return value;
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _entries.Clear();
    }

    public void Invalidate(IEnumerable<string> tags)
    {
        foreach (var tag in tags) _tags.AddOrUpdate(tag, 1, (_, version) => version + 1);
    }

    private long[] Versions(IReadOnlyList<string> tags)
    {
        var versions = new long[tags.Count + 1];
        versions[0] = Interlocked.Read(ref _version);
        for (var i = 0; i < tags.Count; i++) versions[i + 1] = _tags.GetValueOrDefault(tags[i]);
        return versions;
    }

    private sealed record Entry(object Value, long[] Versions, DateTimeOffset ExpiresAt);
}

// De qué datos depende cada lectura guardada. Cada guardado sube la versión de lo que toca (SearchIndexInvalidator): un
// partido, la de los partidos de su temporada y la de todos; una clasificación, igual; un árbitro, una designación o una
// nota, la de los árbitros; ligas, temporadas, zonas y equipos, la de la estructura.
public static class PublicTags
{
    public const string Structure = "estructura";
    public const string Referees = "arbitros";
    public const string AllMatches = "partidos";
    public const string AllStandings = "clasificaciones";

    public static string Matches(Guid seasonId) => $"partidos:{seasonId:N}";
    public static string Standings(Guid seasonId) => $"clasificaciones:{seasonId:N}";
}
