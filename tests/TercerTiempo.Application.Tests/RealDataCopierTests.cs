using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.Search;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.RealData;
using TercerTiempo.Infrastructure.Teams;

namespace TercerTiempo.Application.Tests;

public class RealDataCopierTests(RealDataSqlFixture fixture) : IClassFixture<RealDataSqlFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [LocalSqlFact]
    public async Task The_environment_gets_the_real_data_and_keeps_what_its_clubs_changed()
    {
        // La base real con una liga, dos equipos, un partido con su árbitro, la clasificación y dos fichas publicadas.
        var year = SeasonYear.Create("2026/2027", new(2026, 8, 1), new(2027, 6, 30));
        var competition = Competition.Create("1ª AsturFútbol", "1ª AsturFútbol", CompetitionLevel.Regional, 5, null, "Asturias", ExternalRef.Create("fa", "1"));
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), ExternalRef.Create("fa", "s1"), year.Id);
        var home = Team.Create("CD Vallobín", "Vallobín", ExternalRef.Create("fa", "t1"));
        var away = Team.Create("Real Titánico", "Titánico", ExternalRef.Create("fa", "t2"));
        away.SetCrest("titanico.png");
        var match = Match.Schedule(season.Id, 8, home.Id, away.Id, Now.AddDays(-4), "Vallobín", ExternalRef.Create("fa", "m1"));
        var edited = RosterEntry.Create(home.Id, season.Id, "Pepe Pérez", null, 7, ExternalRef.Create("fa", "p1"), Now);
        var hidden = RosterEntry.Create(home.Id, season.Id, "Juan López", null, 8, ExternalRef.Create("fa", "p2"), Now);
        var standing = Standing.Create(season.Id, home.Id, new StandingLine(1, 7, 5, 1, 1, 15, 5, 16), Now);
        var referee = Referee.Create("Árbitro Ejemplo", null, Now);
        var official = MatchOfficial.Assign(match.Id, referee.Id, OfficialRole.Referee, "acta", Now);

        await using (var real = fixture.Real())
        {
            real.AddRange(year, competition, home, away, match, edited, hidden, standing, referee, official);
            real.Add(new TeamCrestImage { Key = "titanico.png", TeamId = away.Id, ContentType = "image/png", Content = [1, 2, 3], SavedAt = Now });
            await real.SaveChangesAsync();
        }

        var first = await fixture.Copier().CopyAsync(default);
        Assert.False(first.Quiet);
        Assert.True(fixture.CrestFiles().Exists("titanico.png"));
        Assert.True((await fixture.Copier().CopyAsync(default)).Quiet);

        // Lo que hacen los clubes en este entorno: su escudo, una ficha corregida, otra oculta, una nueva y una nota al
        // árbitro.
        await using (var db = fixture.Environment())
        {
            (await db.Set<Team>().SingleAsync(t => t.Id == home.Id)).SetCrest("vallobin-club.png", fromClub: true);
            (await db.Set<RosterEntry>().SingleAsync(r => r.Id == edited.Id)).UpdateByClub("José Pérez", null, 10, Now);
            (await db.Set<RosterEntry>().SingleAsync(r => r.Id == hidden.Id)).Hide();
            db.Add(RosterEntry.CreateByClub(home.Id, season.Id, "Fichado por el club", null, 22, Now));
            var club = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
            db.Add(club);
            db.Add(RefereeRating.Rate(official, home.Id, 7, null, new RefereeRater(club.Id, null, null, "CD Vallobín"), Now));
            await db.SaveChangesAsync();
        }

        // Y lo que cambia en las webs: el nombre y el escudo del equipo, las fichas, y desaparecen la clasificación y el
        // árbitro del partido.
        await using (var real = fixture.Real())
        {
            var team = await real.Set<Team>().SingleAsync(t => t.Id == home.Id);
            team.UpdateDetails("CD Vallobín Oviedo", "Vallobín");
            team.SetCrest("vallobin-web.png");
            (await real.Set<RosterEntry>().SingleAsync(r => r.Id == edited.Id)).Update("Pepe Pérez García", null, 7, Now);
            (await real.Set<RosterEntry>().SingleAsync(r => r.Id == hidden.Id)).Update("Juan López Díaz", null, 8, Now);
            real.Remove(await real.Set<Standing>().SingleAsync());
            real.Remove(await real.Set<MatchOfficial>().SingleAsync());
            real.Remove(await real.Set<Referee>().SingleAsync());
            await real.SaveChangesAsync();
        }

        await fixture.Copier().CopyAsync(default);

        await using var after = fixture.Environment();
        var copiedTeam = await after.Set<Team>().SingleAsync(t => t.Id == home.Id);
        Assert.Equal("CD Vallobín Oviedo", copiedTeam.Name);
        Assert.Equal("vallobin-club.png", copiedTeam.CrestKey);
        Assert.True(copiedTeam.CrestFromClub);

        var entries = await after.Set<RosterEntry>().ToListAsync();
        Assert.Equal("José Pérez", entries.Single(r => r.Id == edited.Id).FullName);
        Assert.Equal("Juan López Díaz", entries.Single(r => r.Id == hidden.Id).FullName);
        Assert.True(entries.Single(r => r.Id == hidden.Id).Hidden);
        Assert.Contains(entries, r => r.FullName == "Fichado por el club");

        Assert.Empty(await after.Set<Standing>().ToListAsync());
        Assert.Empty(await after.Set<MatchOfficial>().ToListAsync());
        // El árbitro ya no está en la base real, pero aquí tiene una nota: se queda.
        Assert.Single(await after.Set<Referee>().ToListAsync());
    }
}

// Una base de entorno y otra de datos reales, cada una con sus migraciones.
public sealed class RealDataSqlFixture : IAsyncLifetime
{
    private string _environment = null!;
    private string _realData = null!;
    private string _crests = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        if (System.Environment.GetEnvironmentVariable("TT_RUN_LOCAL_SQL_TESTS") != "1") return;
        var suffix = Guid.NewGuid().ToString("N");
        _environment = $"Server=localhost;Database=TercerTiempo_CopyVerification_{suffix};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10";
        _realData = $"Server=localhost;Database=TercerTiempo_CopyVerificationReal_{suffix};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10";
        _crests = Path.Combine(Path.GetTempPath(), $"tercertiempo-crests-{suffix}");
        var realData = _realData;
        _services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddSingleton<SearchIndexHolder>()
            .AddDbContext<RealDataDbContext>(o => o.UseSqlServer(realData))
            .BuildServiceProvider();

        await using (var db = Environment()) await db.Database.MigrateAsync();
        await using (var real = new RealDataDbContext(new DbContextOptionsBuilder<RealDataDbContext>().UseSqlServer(_realData).Options))
            await real.Database.MigrateAsync();
    }

    public AppDbContext Environment() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_environment).Options);

    public AppDbContext Real() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_realData).Options);

    internal FileSystemCrestStorage CrestFiles() => new(Options.Create(new CrestOptions { Directory = _crests }));

    internal RealDataCopier Copier() => new(
        new RealDataConnections(_environment, _realData),
        _services.GetRequiredService<IServiceScopeFactory>(),
        CrestFiles(),
        NullLogger<RealDataCopier>.Instance);

    public async Task DisposeAsync()
    {
        if (_environment is null) return;
        await using (var db = Environment()) await db.Database.EnsureDeletedAsync();
        await using (var real = Real()) await real.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
        if (Directory.Exists(_crests)) Directory.Delete(_crests, recursive: true);
    }
}
