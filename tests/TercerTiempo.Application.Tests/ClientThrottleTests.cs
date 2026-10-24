using Microsoft.Extensions.Options;
using TercerTiempo.Infrastructure.Security;

namespace TercerTiempo.Application.Tests;

public class ClientThrottleTests
{
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (ClientThrottle Throttle, ManualClock Clock) Create(int perWindow = 3, int perEndpoint = 2)
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 21, 18, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new ThrottlingOptions
        {
            WindowSeconds = 60,
            RequestsPerWindow = perWindow,
            RequestsPerEndpointPerWindow = perEndpoint,
            BanMinutes = [5, 30, 1440],
            StrikeMemoryHours = 24 * 7,
        });
        return (new ClientThrottle(options, clock), clock);
    }

    [Fact]
    public void Exceeding_the_window_limit_bans_the_ip_five_minutes_and_then_escalates()
    {
        var (throttle, clock) = Create();

        Assert.True(throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).Allowed);
        Assert.True(throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).Allowed);
        Assert.True(throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).Allowed);

        var limited = throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);
        Assert.Equal(ThrottleReason.Limited, limited.Reason);
        Assert.Equal(TimeSpan.FromMinutes(5), limited.RetryAfter);
        Assert.Equal(1, limited.Strikes);

        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(ThrottleReason.Banned, throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).Reason);

        // Pasado el bloqueo vuelve a entrar; otra IP nunca se vio afectada.
        clock.Now += TimeSpan.FromMinutes(4);
        Assert.True(throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).Allowed);
        Assert.True(throttle.Check("2.2.2.2", "GET /a", countEndpoint: false).Allowed);

        // Reincidencias: 30 min, 1 día y después el doble cada vez.
        for (var i = 0; i < 3; i++) throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);
        Assert.Equal(TimeSpan.FromMinutes(30), throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).RetryAfter);

        clock.Now += TimeSpan.FromMinutes(31);
        for (var i = 0; i < 3; i++) throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);
        Assert.Equal(TimeSpan.FromDays(1), throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).RetryAfter);

        clock.Now += TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 3; i++) throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);
        Assert.Equal(TimeSpan.FromDays(2), throttle.Check("1.1.1.1", "GET /a", countEndpoint: false).RetryAfter);
    }

    [Fact]
    public void The_per_endpoint_limit_is_tighter_than_the_global_one()
    {
        var (throttle, _) = Create(perWindow: 100, perEndpoint: 2);

        Assert.True(throttle.Check("1.1.1.1", "GET /a").Allowed);
        Assert.True(throttle.Check("1.1.1.1", "GET /b").Allowed);
        Assert.True(throttle.Check("1.1.1.1", "GET /a").Allowed);

        var limited = throttle.Check("1.1.1.1", "GET /a");
        Assert.Equal(ThrottleReason.Limited, limited.Reason);
        Assert.Equal(2, limited.Limit);
    }

    [Fact]
    public void Strikes_are_forgotten_after_the_memory_period()
    {
        var (throttle, clock) = Create();

        for (var i = 0; i < 4; i++) throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);
        clock.Now += TimeSpan.FromDays(8);

        for (var i = 0; i < 3; i++) throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);
        var again = throttle.Check("1.1.1.1", "GET /a", countEndpoint: false);

        Assert.Equal(1, again.Strikes);
        Assert.Equal(TimeSpan.FromMinutes(5), again.RetryAfter);
    }

    [Fact]
    public void Ban_duration_doubles_beyond_the_configured_steps_up_to_the_cap()
    {
        var settings = new ThrottlingOptions { BanMinutes = [5, 30, 1440], MaxBanMinutes = 4 * 1440 };

        Assert.Equal(TimeSpan.FromMinutes(5), ClientThrottle.BanDuration(settings, 1));
        Assert.Equal(TimeSpan.FromMinutes(1440), ClientThrottle.BanDuration(settings, 3));
        Assert.Equal(TimeSpan.FromMinutes(2880), ClientThrottle.BanDuration(settings, 4));
        Assert.Equal(TimeSpan.FromMinutes(4 * 1440), ClientThrottle.BanDuration(settings, 6));
    }
}
