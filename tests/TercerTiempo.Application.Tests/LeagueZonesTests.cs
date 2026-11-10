using System.Reflection;
using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Jobs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

namespace TercerTiempo.Application.Tests;

// Plazas de cada liga (ascenso, promoción, descenso) tal como las marca futbolasturiano.es en su clasificación.
public class LeagueZonesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 18, 0, 0, TimeSpan.Zero);

    // Lo esencial de la página de la clasificación de 1ª AsturFútbol 2026/2027: el estilo de cada fila marcada y la leyenda.
    private const string FirstAsturfutbolPage = """
        <style type="text/css">.sp_highlight_places .sp_color { display: inline-block; }
        table.sp-league-table-17361 tr.sp-row-no-0 td.data-rank{
            background: #fdfd96;
        }
        table.sp-league-table-17361 tr.sp-row-no-1 td.data-rank{ background: #fdfd96; }
        table.sp-league-table-17361 tr.sp-row-no-2 td.data-rank{ background: #FDFD96; }
        table.sp-league-table-17361 tr.sp-row-no-14 td.data-rank{ background: #cb010a; }
        table.sp-league-table-17361 tr.sp-row-no-15 td.data-rank{ background: #cb010a; }
        table.sp-league-table-17361 tr.sp-row-no-16 td.data-rank{ background: #cb010a; }
        table.sp-league-table-17361 tr.sp-row-no-17 td.data-rank{ background: #cb010a; }
        table.sp-league-table-17361 tr.sp-row-no-18 td.data-rank{ background: #cb010a; }
        table.sp-league-table-17361 tr.sp-row-no-19 td.data-rank{ background: #cb010a; }
        table.sp-league-table-99999 tr.sp-row-no-5 td.data-rank{ background: #ffb347; }
        </style>
        <table class="sp-league-table sp-league-table-17361"><tbody><tr class="sp-row-no-0 odd"><td class="data-rank">1</td></tr></tbody></table>
        <div class="sp_highlight_places"><div class="sp_highlight_place"><div class="sp_color" style="background-color:#fdfd96">&nbsp;</div><div class="sp_desc">Ascenso directo</div></div><div class="sp_highlight_place"><div class="sp_color" style="background-color:#cb010a">&nbsp;</div><div class="sp_desc">Descenso</div></div></div>
        """;

    [Fact]
    public void The_places_are_read_from_the_table_page()
    {
        var zones = LeagueTablePage.ParseZones(FirstAsturfutbolPage, 17361);

        Assert.Equal([1, 2, 3, 15, 16, 17, 18, 19, 20], zones.Select(z => z.Position));
        Assert.All(zones.Take(3), z => Assert.Equal(("Ascenso directo", "#fdfd96"), (z.Label, z.Color)));
        Assert.All(zones.Skip(3), z => Assert.Equal(("Descenso", "#cb010a"), (z.Label, z.Color)));
        Assert.Empty(LeagueTablePage.ParseZones("<table class=\"sp-league-table-1\"></table>", 1));
    }

    [Fact]
    public async Task The_sync_stores_the_places_and_only_writes_when_they_change()
    {
        var competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 1, null, "Asturias", ExternalRef.Create("futbolasturiano", "100"));
        var season = competition.AddSeason("2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var provider = new FakeProvider { Zones = [new(1, "Ascenso directo", "#fdfd96"), new(20, "Descenso", "#cb010a")] };
        var store = new InMemorySeasonZoneStore();
        var unitOfWork = new CountingUnitOfWork();
        var competitions = Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition]));
        var progress = new JobProgress(TimeProvider.System);
        var handler = new SyncLeagueZonesHandler(provider, competitions, store, progress, unitOfWork, TimeProvider.System);

        Assert.Equal(new LeagueZonesSynced(1, 1, 0), await handler.Handle(new SyncLeagueZonesCommand(), default));
        // Lo que se ve en directo en la administración.
        var live = progress.Snapshot(JobNames.LeagueZones);
        Assert.Equal("1ª AsturFútbol: plazas nuevas · Ascenso directo 1.º · Descenso 20.º", live.Feed[0].Text);
        Assert.Equal((1, 1), (live.Done, live.Total));
        Assert.Equal([(1, LeagueZoneKind.DirectPromotion), (20, LeagueZoneKind.Relegation)], store.Zones.Select(z => (z.Position, z.Kind)));

        Assert.Equal(new LeagueZonesSynced(1, 0, 0), await handler.Handle(new SyncLeagueZonesCommand(), default));
        Assert.Equal(1, unitOfWork.Saves);

        provider.Zones = [new(1, "Ascenso directo", "#fdfd96"), new(2, "Promoción de ascenso", "#ffb347")];
        await handler.Handle(new SyncLeagueZonesCommand(), default);
        Assert.Equal([(1, LeagueZoneKind.DirectPromotion), (2, LeagueZoneKind.PromotionPlayoff)], store.Zones.OrderBy(z => z.Position).Select(z => (z.Position, z.Kind)));

        provider.Zones = null;
        Assert.Equal(new LeagueZonesSynced(1, 0, 1), await handler.Handle(new SyncLeagueZonesCommand(), default));
        Assert.Equal(2, store.Zones.Count);
    }

    private sealed class FakeProvider : IFootballDataProvider
    {
        public IReadOnlyList<LeagueZoneData>? Zones { get; set; }
        public string ProviderName => "futbolasturiano";
        public Task<IReadOnlyList<LeagueZoneData>?> GetLeagueZonesAsync(string competitionExternalId, CancellationToken cancellationToken) => Task.FromResult(Zones);
        public Task<IReadOnlyList<CompetitionSnapshot>> GetCompetitionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeamSnapshot>> GetTeamsAsync(string competitionExternalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MatchSnapshot>> GetMatchesAsync(string competitionExternalId, IReadOnlyCollection<MatchSnapshot> known, MatchWindow? window, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StandingSnapshot>> GetStandingsAsync(string competitionExternalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<RosterEntrySnapshot>> GetRosterAsync(string teamExternalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MatchOfficialData>?> GetMatchOfficialsAsync(string matchExternalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InMemorySeasonZoneStore : ISeasonZoneStore
    {
        public List<SeasonZone> Zones { get; } = [];
        public Task<IReadOnlyList<SeasonZone>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SeasonZone>>(Zones.Where(z => z.SeasonId == seasonId).OrderBy(z => z.Position).ToList());
        public void Add(SeasonZone zone) => Zones.Add(zone);
        public void Remove(SeasonZone zone) => Zones.Remove(zone);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var stub = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)stub).Call = call;
        return stub;
    }
}
