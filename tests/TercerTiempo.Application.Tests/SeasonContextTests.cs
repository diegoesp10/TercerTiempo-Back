using TercerTiempo.Application.Common;
using TercerTiempo.Application.Seasons;
using TercerTiempo.Domain.Competitions;

namespace TercerTiempo.Application.Tests;

public class SeasonContextTests
{
    private readonly SeasonYear _old = SeasonYear.Create("2025/2026", new(2025, 8, 1), new(2026, 6, 30));
    private readonly SeasonYear _current = SeasonYear.Create("2026/2027", new(2026, 8, 1), new(2027, 6, 30));

    private SeasonContext Context(string? requested)
    {
        SeasonYear.MarkCurrent([_old, _current], _current.Id);
        return new SeasonContext(new Store([_current, _old]), new Request(requested));
    }

    [Theory]
    [InlineData(null, "2026/2027")]
    [InlineData("2025-2026", "2025/2026")]
    [InlineData("2025/2026", "2025/2026")]
    [InlineData("1999/2000", "2026/2027")]
    public async Task The_request_picks_the_season_by_name_and_otherwise_gets_the_current_one(string? requested, string expected)
    {
        Assert.Equal(expected, (await Context(requested).SelectedAsync())!.Label);
    }

    [Fact]
    public async Task A_season_can_be_picked_by_id_and_past_seasons_are_read_only()
    {
        var context = Context(_old.Id.ToString());
        Assert.Equal(_old.Id, (await context.SelectedAsync())!.Id);

        var closed = await Assert.ThrowsAsync<ConflictException>(() => context.EnsureEditableAsync());
        Assert.Equal(TercerTiempo.Domain.Common.Errors.ErrorCode.SeasonClosed, closed.Code);

        await Context(null).EnsureEditableAsync();
    }

    [Fact]
    public void Each_league_reads_its_own_season_of_the_selected_year()
    {
        var league = Competition.Create("1ª AsturFútbol", "Senior", CompetitionLevel.Regional, 5, null, "Asturias", null);
        var past = league.AddSeason("2025/2026", new(2025, 8, 1), new(2026, 6, 30), null, _old.Id);
        var now = league.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null, _current.Id);
        league.MarkCurrent(now.Id);

        Assert.Equal(past.Id, Resolvers.SeasonFor(league, _old)!.Id);
        Assert.Equal(now.Id, Resolvers.SeasonFor(league, null)!.Id);
        Assert.Null(Resolvers.SeasonFor(league, SeasonYear.Create("2024/2025", new(2024, 8, 1), new(2025, 6, 30))));
    }

    private sealed class Store(IReadOnlyList<SeasonYear> all) : ISeasonYearStore
    {
        public Task<IReadOnlyList<SeasonYear>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(all);
        public Task<IReadOnlyList<SeasonYear>> GetAllForUpdateAsync(CancellationToken cancellationToken = default) => Task.FromResult(all);
        public void Add(SeasonYear seasonYear) => throw new NotSupportedException();
    }

    private sealed record Request(string? Requested) : ISeasonRequest;
}
