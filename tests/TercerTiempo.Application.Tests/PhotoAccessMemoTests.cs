using TercerTiempo.Application.Common;

namespace TercerTiempo.Application.Tests;

public class PhotoAccessMemoTests
{
    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task The_decision_is_reused_for_a_minute_and_then_asked_again()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 26, 18, 0, 0, TimeSpan.Zero));
        var memo = new PhotoAccessMemo(clock);
        var asked = 0;
        var allowed = true;
        Task<bool> Decide()
        {
            asked++;
            return Task.FromResult(allowed);
        }

        Assert.True(await memo.GetAsync("cuenta|equipo", Decide));
        allowed = false;
        clock.Now += TimeSpan.FromSeconds(59);
        Assert.True(await memo.GetAsync("cuenta|equipo", Decide));
        Assert.Equal(1, asked);

        clock.Now += TimeSpan.FromSeconds(2);
        Assert.False(await memo.GetAsync("cuenta|equipo", Decide));
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task Each_account_and_team_has_its_own_decision()
    {
        var memo = new PhotoAccessMemo(TimeProvider.System);

        Assert.True(await memo.GetAsync("cuenta|equipo", () => Task.FromResult(true)));
        Assert.False(await memo.GetAsync("otra|equipo", () => Task.FromResult(false)));
        Assert.True(await memo.GetAsync("cuenta|equipo", () => Task.FromResult(false)));
    }
}
