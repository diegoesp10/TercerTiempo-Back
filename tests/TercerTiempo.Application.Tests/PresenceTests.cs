using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Presence;

namespace TercerTiempo.Application.Tests;

// Gente en sesión: cada inicio de sesión con su última señal, solo en memoria.
public class PresenceTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_session_is_online_then_idle_and_disappears_after_thirty_minutes()
    {
        var board = new PresenceBoard(_clock);
        board.Touch("s1", Guid.NewGuid(), AccountType.Club, "club@example.com", "/", "Chrome · Windows", new PresenceProfile("CD Vallobín", "CD Vallobín"));

        Assert.Equal(PresenceStatus.Online, Assert.Single(board.Overview(null).Sessions).Status);

        _clock.Advance(TimeSpan.FromMinutes(3));
        var idle = board.Overview(null);
        Assert.Equal((0, 1), (idle.Online, idle.Idle));

        _clock.Advance(TimeSpan.FromMinutes(28));
        Assert.Empty(board.Overview(null).Sessions);
    }

    [Fact]
    public void Each_heartbeat_keeps_when_it_started_and_moves_the_page_and_the_last_signal()
    {
        var board = new PresenceBoard(_clock);
        var started = _clock.GetUtcNow();
        board.Touch("s1", Guid.NewGuid(), AccountType.Coach, "coach@example.com", "/", "Chrome · Windows", new PresenceProfile("Abel Entrenador", "CD Vallobín"));

        _clock.Advance(TimeSpan.FromMinutes(1));
        board.Touch("s1", Guid.NewGuid(), AccountType.Coach, "coach@example.com", "/plantillas/1", "Chrome · Windows", null);

        var session = Assert.Single(board.Overview(null).Sessions);
        Assert.Equal((started, _clock.GetUtcNow(), "/plantillas/1", "Abel Entrenador"), (session.StartedAt, session.LastSeenAt, session.Path, session.Name));
        Assert.False(board.NeedsProfile("s1"));
        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.True(board.NeedsProfile("s1"));
    }

    [Fact]
    public void The_search_looks_at_name_email_and_teams_without_accents_and_counts_by_account_type()
    {
        var board = new PresenceBoard(_clock);
        board.Touch("club", Guid.NewGuid(), AccountType.Club, "vallobin@example.com", "/", "Chrome · Windows", new PresenceProfile("CD Vallobín", "CD Vallobín"));
        board.Touch("coach", Guid.NewGuid(), AccountType.Coach, "abel@example.com", "/", "Safari · iPhone", new PresenceProfile("Abel Entrenador", "TSK Roces"));
        board.Touch("player", Guid.NewGuid(), AccountType.Player, "pelayo@example.com", "/", "Chrome · Android", new PresenceProfile("Pelayo Fernández", "CD Vallobín"));

        var vallobin = board.Overview("vallobin");
        Assert.Equal(["Pelayo Fernández", "CD Vallobín"], vallobin.Sessions.Select(s => s.Name).Order().Reverse());
        Assert.Equal((1, 1, 1, 2), (vallobin.Clubs, vallobin.Coaches, vallobin.Players, vallobin.Matching));
        Assert.Equal("Abel Entrenador", Assert.Single(board.Overview("abel@").Sessions).Name);

        board.End("coach");
        Assert.Equal(2, board.Overview(null).Sessions.Count);
    }

    [Fact]
    public async Task Only_accounts_are_tracked_never_the_administrator()
    {
        var board = new PresenceBoard(_clock);
        var handler = new RecordPresenceHandler(new Admin(), board, null!, null!, null!, null!, null!, _clock);

        Assert.False(await handler.Handle(new RecordPresenceCommand("/jobs", "Mozilla/5.0"), default));
        Assert.Empty(board.Overview(null).Sessions);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36", "Chrome · Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0 Mobile Safari/537.36", "Samsung Internet · Android")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.5; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox · Mac")]
    [InlineData(null, "Desconocido")]
    public void The_device_is_named_by_browser_and_system(string? userAgent, string expected) =>
        Assert.Equal(expected, DeviceLabel.From(userAgent));

    private sealed record Admin : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public AccountType? AccountType => null;
        public Guid? PlayerId => null;
        public Guid? CoachId => null;
        public Guid? ClubId => null;
        public bool IsAdmin => true;
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
