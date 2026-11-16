using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Tests;

public class DataLoadingTests
{
    [Fact]
    public async Task Public_data_is_read_once_until_it_changes_or_expires()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
        var cache = new PublicDataCache(clock);
        var reads = 0;
        Task<string> Load(CancellationToken _) => Task.FromResult($"lectura {++reads}");

        Assert.Equal("lectura 1", await cache.GetAsync("liga", Load, default));
        Assert.Equal("lectura 1", await cache.GetAsync("liga", Load, default));

        cache.Invalidate();
        Assert.Equal("lectura 2", await cache.GetAsync("liga", Load, default));

        clock.Now += PublicDataCache.Lifetime + TimeSpan.FromSeconds(1);
        Assert.Equal("lectura 3", await cache.GetAsync("liga", Load, default));
    }

    [Fact]
    public async Task What_was_read_while_the_data_changed_is_not_kept()
    {
        var cache = new PublicDataCache(TimeProvider.System);
        var reads = 0;

        var first = await cache.GetAsync("árbitros", _ =>
        {
            reads++;
            cache.Invalidate();
            return Task.FromResult("antigua");
        }, default);
        var second = await cache.GetAsync("árbitros", _ => Task.FromResult($"nueva {++reads}"), default);

        Assert.Equal(("antigua", "nueva 2"), (first, second));
    }

    [Fact]
    public async Task Queries_run_inside_the_read_scope_and_leave_it_afterwards()
    {
        var scope = new CountingScope();
        var handler = new ReadOnlyQueryHandler<Ping, int>(new PingHandler(scope), scope);

        Assert.Equal(1, await handler.Handle(new Ping(), default));
        Assert.Equal((1, 1), (scope.Opened, scope.Closed));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReadOnlyQueryHandler<Ping, int>(new PingHandler(scope, fail: true), scope).Handle(new Ping(), default));
        Assert.Equal((2, 2), (scope.Opened, scope.Closed));
    }

    [Fact]
    public void The_calendar_asks_for_at_most_120_days()
    {
        var from = new DateOnly(2026, 11, 1);
        TrainingClock.EnsureRange(from, from.AddDays(TrainingClock.MaxRangeDays - 1));
        TrainingClock.EnsureRange(from, from.AddDays(-3));

        var tooLong = Assert.Throws<DomainException>(() => TrainingClock.EnsureRange(from, from.AddDays(TrainingClock.MaxRangeDays)));
        Assert.Equal(ErrorCode.CommonOutOfRange, tooLong.Code);
    }

    private sealed record Ping : IQuery<int>;

    private sealed class PingHandler(CountingScope scope, bool fail = false) : IQueryHandler<Ping, int>
    {
        public Task<int> Handle(Ping query, CancellationToken cancellationToken) =>
            fail ? throw new InvalidOperationException() : Task.FromResult(scope.Opened - scope.Closed);
    }

    private sealed class CountingScope : IReadScope
    {
        public int Opened { get; private set; }
        public int Closed { get; private set; }

        public IDisposable Begin()
        {
            Opened++;
            return new Closer(this);
        }

        private sealed class Closer(CountingScope scope) : IDisposable
        {
            public void Dispose() => scope.Closed++;
        }
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
