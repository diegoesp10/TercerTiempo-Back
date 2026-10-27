using TercerTiempo.Application.Alerts;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Tests;

// Avisos de caducidad en la campanita del club: 15 y 5 días antes del fin de una licencia que no se renueva.
public class LicenseAlertTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDay = DateOnly.FromDateTime(Start.UtcDateTime);

    [Fact]
    public async Task A_trial_without_contract_warns_15_and_5_days_before_and_only_once_each()
    {
        var f = new Fixture();
        var license = ClubLicense.Start(f.Club.Id, StartDay, 30, Start);

        await f.SyncOn(license, StartDay.AddDays(10));
        Assert.Empty(f.Alerts);

        await f.SyncOn(license, StartDay.AddDays(16));
        await f.SyncOn(license, StartDay.AddDays(17));
        var first = Assert.Single(f.Alerts);
        Assert.Equal(("Tu prueba acaba en 14 días", license.ValidUntil), (first.Title, first.DueOn));

        await f.SyncOn(license, StartDay.AddDays(25));
        Assert.Equal(2, f.Alerts.Count);
        Assert.Equal("Tu prueba acaba en 5 días", f.Alerts[1].Title);
        Assert.Equal(2, ClubAlerts.ToDto(f.Alerts).Unread);
    }

    [Fact]
    public async Task A_cancelled_renewal_close_to_its_end_gets_only_the_nearest_warning()
    {
        var f = new Fixture();
        var license = ClubLicense.Start(f.Club.Id, StartDay, 0, Start);
        license.Subscribe(BillingInterval.Monthly, 1, "mock", "sub_1", null, Start);
        license.RecordPaidPeriod(StartDay.AddDays(20), Start);
        license.CancelRenewal(Start);

        await f.SyncOn(license, StartDay.AddDays(17));

        var alert = Assert.Single(f.Alerts);
        Assert.Equal("Tu licencia acaba en 3 días", alert.Title);
        Assert.EndsWith(":5", alert.Key);
        Assert.Equal("/licencias", alert.Link);
    }

    [Fact]
    public async Task Resuming_the_renewal_resolves_the_warnings_and_a_renewing_license_gets_none()
    {
        var f = new Fixture();
        var license = ClubLicense.Start(f.Club.Id, StartDay, 0, Start);
        license.Subscribe(BillingInterval.Monthly, 0, "mock", "sub_1", null, Start);
        license.RecordPaidPeriod(StartDay.AddDays(20), Start);
        license.CancelRenewal(Start);
        await f.SyncOn(license, StartDay.AddDays(10));
        Assert.Single(f.Alerts);

        license.ResumeRenewal(StartDay.AddDays(11), Start);
        var result = await f.SyncOn(license, StartDay.AddDays(11));

        Assert.Equal((0, 1), (result.Raised, result.Resolved));
        Assert.True(f.Alerts[0].IsResolved);
        Assert.Equal(0, ClubAlerts.ToDto(f.Alerts).Unread);

        await f.SyncOn(license, StartDay.AddDays(18));
        Assert.Single(f.Alerts);
    }

    [Fact]
    public async Task Without_alert_days_nothing_is_raised()
    {
        var f = new Fixture();
        f.Settings.Update("EUR", 30, 2, 30, [], [new(LicenseItem.Club, 490, 4900, null, null), new(LicenseItem.StaffSeat, 250, 2500, null, null)], Start);
        var license = ClubLicense.Start(f.Club.Id, StartDay, 30, Start);

        await f.SyncOn(license, StartDay.AddDays(28));

        Assert.Empty(f.Alerts);
    }

    private sealed class Fixture
    {
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Start);
        public LicensingSettings Settings { get; } = LicensingSettings.CreateDefault(Start);
        public List<Alert> Alerts { get; } = [];

        public Task<LicenseAlertsSynced> SyncOn(ClubLicense license, DateOnly day) =>
            new LicenseAlertSync(new Store(Alerts), new Catalog(Settings), new Clock(new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero)))
                .SyncAsync(Club, license, null, default);
    }

    private sealed class Store(List<Alert> alerts) : IAlertStore
    {
        public Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(alerts.FirstOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<Alert>> GetByClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Alert>>(alerts.Where(a => a.ClubId == clubId).ToList());
        public Task<IReadOnlyList<Alert>> GetByKindAsync(AlertKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Alert>>(alerts.Where(a => a.Kind == kind).ToList());
        public void Add(Alert alert) => alerts.Add(alert);
    }

    private sealed class Catalog(LicensingSettings settings) : ILicensingCatalog
    {
        public Task<LicensingSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public void Invalidate() { }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
