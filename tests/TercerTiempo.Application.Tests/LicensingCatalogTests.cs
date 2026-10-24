using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Tests;

public class LicensingCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class CountingStore(LicensingSettings? settings) : ILicensingSettingsStore
    {
        public int Reads { get; private set; }
        public LicensingSettings? Settings { get; set; } = settings;

        public Task<LicensingSettings?> GetAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Settings);
        }

        public Task<LicensingSettings?> GetForUpdateAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);

        public void Add(LicensingSettings settings) => Settings = settings;
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Reads_the_database_once_until_the_cache_expires_or_is_invalidated()
    {
        var store = new CountingStore(LicensingSettings.CreateDefault(Now));
        var clock = new MovableClock(Now);
        var catalog = new LicensingCatalog(store, new LicensingCatalogCache(), clock);

        await catalog.GetAsync();
        await catalog.GetAsync();
        Assert.Equal(1, store.Reads);

        catalog.Invalidate();
        await catalog.GetAsync();
        Assert.Equal(2, store.Reads);

        clock.Now += LicensingCatalogCache.Lifetime;
        await catalog.GetAsync();
        Assert.Equal(3, store.Reads);
    }

    [Fact]
    public async Task Without_a_row_it_falls_back_to_the_launch_prices()
    {
        var catalog = new LicensingCatalog(new CountingStore(null), new LicensingCatalogCache(), new MovableClock(Now));

        var settings = await catalog.GetAsync();

        Assert.Equal(990, settings.PriceCents(LicensePlan.Basic, BillingInterval.Monthly));
        Assert.Equal(30, settings.TrialDays);
    }
}
