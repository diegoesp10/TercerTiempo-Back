using Microsoft.Extensions.Options;

namespace TercerTiempo.Infrastructure.Security;

public sealed class ThrottlingOptions
{
    public const string SectionName = "Throttling";

    public bool Enabled { get; set; } = true;

    public int WindowSeconds { get; set; } = 60;

    public int RequestsPerWindow { get; set; } = 300;

    public int RequestsPerEndpointPerWindow { get; set; } = 60;

    public int[] BanMinutes { get; set; } = [5, 30, 1440];

    public int MaxBanMinutes { get; set; } = 30 * 1440;

    public int StrikeMemoryHours { get; set; } = 24 * 7;

    public bool TrustForwardedHeaders { get; set; }

    public string[] ExemptRoutes { get; set; } = [];
}

public enum ThrottleReason
{
    Allowed,

    Limited,

    Banned,
}

public sealed record ThrottleDecision(ThrottleReason Reason, TimeSpan RetryAfter, int Strikes, int Limit, int WindowSeconds)
{
    public bool Allowed => Reason == ThrottleReason.Allowed;
}

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
