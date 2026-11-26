using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Search;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Search;

namespace TercerTiempo.Application.Tests;

public class SearchIndexHolderTests
{
    [Fact]
    public async Task A_search_never_waits_for_the_index_to_be_rebuilt_except_the_first()
    {
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero));
        var source = new GatedCorpusSource();
        var holder = new SearchIndexHolder(
            new ServiceCollection().AddSingleton<ISearchCorpusSource>(source).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock, NullLogger<SearchIndexHolder>.Instance);

        source.Release();
        Assert.Equal(1, (await holder.GetAsync(default)).Count);

        // Alguien acaba de buscar: al cambiar los datos se empieza a rehacer y mientras tanto se busca en el anterior.
        clock.Now += SearchIndexHolder.MinRebuildGap + TimeSpan.FromSeconds(1);
        holder.Invalidate();
        var during = holder.GetAsync(default);
        Assert.True(during.IsCompletedSuccessfully);
        Assert.Equal(1, (await during).Count);

        source.Release();
        Assert.Equal(2, await CountWhenRebuilt(holder, 2));

        // Sin búsquedas recientes, un cambio no rehace nada hasta que alguien busca (y esa búsqueda tampoco espera).
        clock.Now += TimeSpan.FromMinutes(11);
        holder.Invalidate();
        Assert.Equal(2, source.Loads);
        Assert.Equal(2, (await holder.GetAsync(default)).Count);

        source.Release();
        Assert.Equal(3, await CountWhenRebuilt(holder, 3));
    }

    [Fact]
    public void Each_save_only_drops_the_public_reads_of_what_it_touches()
    {
        var now = DateTimeOffset.UtcNow;
        var season = Guid.NewGuid();
        var team = Team.Create("Equipo", "EQ", null);
        var match = Match.Schedule(season, 1, team.Id, Guid.NewGuid(), now, null, null);
        var standing = Standing.Create(season, team.Id, new StandingLine(1, 4, 3, 1, 0, 6, 3, 10), now);

        Assert.Equal([PublicTags.Matches(season), PublicTags.AllMatches], SearchIndexInvalidator.PublicTagsOf(match));
        Assert.Equal([PublicTags.Standings(season), PublicTags.AllStandings], SearchIndexInvalidator.PublicTagsOf(standing));
        Assert.Equal([PublicTags.Referees], SearchIndexInvalidator.PublicTagsOf(Referee.Create("Árbitro", null, now)));
        Assert.Equal([PublicTags.Structure], SearchIndexInvalidator.PublicTagsOf(team));
        Assert.Empty(SearchIndexInvalidator.PublicTagsOf(Squad.Create(team.Id, season, Guid.NewGuid(), now)));
    }

    // Hasta 15 s: si un cambio llega mientras se rehace, el siguiente espera el hueco mínimo entre dos (en tiempo real).
    private static async Task<int> CountWhenRebuilt(SearchIndexHolder holder, int expected)
    {
        for (var i = 0; i < 600; i++)
        {
            var count = (await holder.GetAsync(default)).Count;
            if (count == expected) return count;
            await Task.Delay(25);
        }
        return (await holder.GetAsync(default)).Count;
    }

    // Cada lectura devuelve un equipo más que la anterior y no termina hasta que la prueba la deja.
    private sealed class GatedCorpusSource : ISearchCorpusSource
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _loads;

        public int Loads => Volatile.Read(ref _loads);

        public void Release() => _gate.Release();

        public async Task<SearchCorpus> LoadAsync(CancellationToken cancellationToken)
        {
            var loads = Interlocked.Increment(ref _loads);
            await _gate.WaitAsync(cancellationToken);
            var teams = Enumerable.Range(1, loads).Select(i => new CorpusTeam(Guid.NewGuid(), $"Equipo {i}", $"E{i}", null, null, null, null)).ToList();
            return new SearchCorpus([], teams, [], [], [], []);
        }
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
