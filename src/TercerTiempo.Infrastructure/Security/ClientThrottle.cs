using Microsoft.Extensions.Options;

namespace TercerTiempo.Infrastructure.Security;

/// <summary>Límite de llamadas por IP y por endpoint, con bloqueos que crecen con cada reincidencia.</summary>
public sealed class ThrottlingOptions
{
    public const string SectionName = "Throttling";

    public bool Enabled { get; set; } = true;

    /// <summary>Tamaño de la ventana en la que se cuentan las llamadas.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Llamadas permitidas por IP en la ventana (a cualquier endpoint).</summary>
    public int RequestsPerWindow { get; set; } = 300;

    /// <summary>Llamadas permitidas por IP a un mismo endpoint (método + ruta) en la ventana.</summary>
    public int RequestsPerEndpointPerWindow { get; set; } = 60;

    /// <summary>Duración de los bloqueos sucesivos en minutos; a partir del último valor se duplica en cada reincidencia.</summary>
    public int[] BanMinutes { get; set; } = [5, 30, 1440];

    /// <summary>Tope de un bloqueo.</summary>
    public int MaxBanMinutes { get; set; } = 30 * 1440;

    /// <summary>Tiempo sin incidencias tras el que una IP vuelve a empezar desde el primer escalón.</summary>
    public int StrikeMemoryHours { get; set; } = 24 * 7;

    /// <summary>Tomar la IP de X-Forwarded-For (solo detrás de un proxy de confianza).</summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Rutas (patrón del endpoint) que no cuentan para el límite por endpoint, p. ej. los escudos.</summary>
    public string[] ExemptRoutes { get; set; } = [];
}

public enum ThrottleReason
{
    /// <summary>Dentro de los límites.</summary>
    Allowed,

    /// <summary>Ha superado el límite de la ventana en esta llamada: empieza un bloqueo.</summary>
    Limited,

    /// <summary>Ya estaba bloqueada.</summary>
    Banned,
}

/// <summary>Resultado de comprobar una llamada: si pasa y, si no, cuánto tiene que esperar y en qué escalón de bloqueo va.</summary>
public sealed record ThrottleDecision(ThrottleReason Reason, TimeSpan RetryAfter, int Strikes, int Limit, int WindowSeconds)
{
    public bool Allowed => Reason == ThrottleReason.Allowed;
}

/// <summary>
/// Contadores en memoria por IP (ventana fija) y por IP + endpoint, y lista de bloqueos. Al superar cualquiera de los
/// dos límites la IP queda bloqueada BanMinutes[n] minutos, donde n es su número de reincidencias recientes; más
/// allá del último escalón el bloqueo se duplica cada vez. Una instancia por proceso (singleton).
/// </summary>
public sealed class ClientThrottle(IOptions<ThrottlingOptions> options, TimeProvider clock)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Window> _byIp = [];
    private readonly Dictionary<string, Window> _byEndpoint = [];
    private readonly Dictionary<string, Ban> _bans = [];
    private int _checks;

    public ThrottlingOptions Settings => options.Value;

    public ThrottleDecision Check(string ip, string endpointKey, bool countEndpoint = true)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var window = TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds));

        lock (_lock)
        {
            if (++_checks % 1000 == 0) Cleanup(now, window);

            if (_bans.TryGetValue(ip, out var ban) && ban.Until > now)
                return new ThrottleDecision(ThrottleReason.Banned, ban.Until - now, ban.Strikes, 0, settings.WindowSeconds);

            var ipCount = Count(_byIp, ip, now, window);
            var endpointCount = countEndpoint ? Count(_byEndpoint, $"{ip}|{endpointKey}", now, window) : 0;

            if (ipCount <= settings.RequestsPerWindow && endpointCount <= settings.RequestsPerEndpointPerWindow)
                return new ThrottleDecision(ThrottleReason.Allowed, TimeSpan.Zero, ban?.Strikes ?? 0, 0, settings.WindowSeconds);

            // Reincidencia: si la última fue hace mucho, vuelve al primer escalón.
            var strikes = ban is not null && now - ban.LastStrikeAt < TimeSpan.FromHours(settings.StrikeMemoryHours) ? ban.Strikes + 1 : 1;
            var duration = BanDuration(settings, strikes);
            _bans[ip] = new Ban(now + duration, strikes, now);

            var limit = ipCount > settings.RequestsPerWindow ? settings.RequestsPerWindow : settings.RequestsPerEndpointPerWindow;
            return new ThrottleDecision(ThrottleReason.Limited, duration, strikes, limit, settings.WindowSeconds);
        }
    }

    /// <summary>Minutos del escalón n (1 = primera vez); pasado el último escalón se duplica, hasta el tope.</summary>
    internal static TimeSpan BanDuration(ThrottlingOptions settings, int strikes)
    {
        var steps = settings.BanMinutes.Length > 0 ? settings.BanMinutes : [5];
        double minutes = strikes <= steps.Length
            ? steps[strikes - 1]
            : steps[^1] * Math.Pow(2, strikes - steps.Length);
        return TimeSpan.FromMinutes(Math.Min(minutes, settings.MaxBanMinutes));
    }

    private static int Count(Dictionary<string, Window> windows, string key, DateTimeOffset now, TimeSpan window)
    {
        if (!windows.TryGetValue(key, out var current) || now - current.Start >= window)
            windows[key] = current = new Window(now);

        return ++current.Count;
    }

    private void Cleanup(DateTimeOffset now, TimeSpan window)
    {
        foreach (var key in _byIp.Where(w => now - w.Value.Start >= window).Select(w => w.Key).ToList()) _byIp.Remove(key);
        foreach (var key in _byEndpoint.Where(w => now - w.Value.Start >= window).Select(w => w.Key).ToList()) _byEndpoint.Remove(key);
        var memory = TimeSpan.FromHours(options.Value.StrikeMemoryHours);
        foreach (var key in _bans.Where(b => b.Value.Until <= now && now - b.Value.LastStrikeAt >= memory).Select(b => b.Key).ToList()) _bans.Remove(key);
    }

    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;
        public int Count { get; set; }
    }

    private sealed record Ban(DateTimeOffset Until, int Strikes, DateTimeOffset LastStrikeAt);
}
