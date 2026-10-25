using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Licensing;

// Precios, prueba y técnicos de cada plan, leídos de la base de datos (LicensingSettings) con una caché corta que se
// vacía al guardarlos desde la administración.
public interface ILicensingCatalog
{
    Task<LicensingSettings> GetAsync(CancellationToken cancellationToken = default);
    void Invalidate();
}

internal sealed class LicensingCatalogCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private (LicensingSettings Settings, DateTimeOffset LoadedAt)? _entry;

    public LicensingSettings? Get(DateTimeOffset now)
    {
        lock (_lock)
            return _entry is { } entry && now - entry.LoadedAt < Lifetime ? entry.Settings : null;
    }

    public void Set(LicensingSettings settings, DateTimeOffset now)
    {
        lock (_lock) _entry = (settings, now);
    }

    public void Clear()
    {
        lock (_lock) _entry = null;
    }
}

internal sealed class LicensingCatalog(ILicensingSettingsStore store, LicensingCatalogCache cache, TimeProvider clock) : ILicensingCatalog
{
    public async Task<LicensingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (cache.Get(now) is { } cached) return cached;

        // La migración siembra la fila; si faltara, se usan los valores por defecto sin escribir nada.
        var settings = await store.GetAsync(cancellationToken) ?? LicensingSettings.CreateDefault(now);
        cache.Set(settings, now);
        return settings;
    }

    public void Invalidate() => cache.Clear();
}
