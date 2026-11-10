using TercerTiempo.Application.Privacy;
using TercerTiempo.Domain.Privacy;
using TercerTiempo.Infrastructure.Privacy;

namespace TercerTiempo.Application.Tests;

// Los informes se calculan en SQL Server: aquí se comprueba que EF los traduce y que cuentan bien.
public class AnalyticsReportsSqlTests(JobSqlFixture fixture) : IClassFixture<JobSqlFixture>
{
    [LocalSqlFact]
    public async Task The_report_counts_views_visitors_searches_clicks_and_clubs_in_sql_server()
    {
        var now = DateTimeOffset.UtcNow;
        var club = Guid.NewGuid();
        var team = Guid.NewGuid();
        string a = Guid.NewGuid().ToString(), b = Guid.NewGuid().ToString(), s = Guid.NewGuid().ToString();

        await using (var db = fixture.Db())
        {
            db.AnalyticsEvents.AddRange(
                AnalyticsEvent.Create(now.AddDays(-1), a, s, AnalyticsEventType.PageView, "/equipos/x", "team", null, AnalyticsEntity.Team, team, AnalyticsActor.Club, club, club),
                AnalyticsEvent.Create(now.AddDays(-1), b, s, AnalyticsEventType.PageView, "/equipos/x", "team", null, AnalyticsEntity.Team, team, AnalyticsActor.Anonymous, null, club),
                AnalyticsEvent.Create(now, a, s, AnalyticsEventType.PageView, "/liga", "league", null, null, null, AnalyticsActor.Club, club, null),
                AnalyticsEvent.Create(now, a, s, AnalyticsEventType.Search, "/equipos", "teams", "caudal", null, null, AnalyticsActor.Club, club, null),
                AnalyticsEvent.Create(now, b, s, AnalyticsEventType.Click, "/liga", "league", "Jornada siguiente", null, null, AnalyticsActor.Anonymous, null, null),
                AnalyticsEvent.Create(now.AddDays(-60), b, s, AnalyticsEventType.PageView, "/liga", "league", null, null, null, AnalyticsActor.Anonymous, null, null));
            db.CookieConsents.AddRange(
                CookieConsent.Record(a, null, true, ConsentAction.AcceptAll, "v1", now),
                CookieConsent.Record(b, null, false, ConsentAction.RejectAll, "v1", now));
            await db.SaveChangesAsync();
        }

        await using var read = fixture.Db();
        var reports = new AnalyticsReports(read);
        var raw = await reports.ReportAsync(new AnalyticsFilter(now.AddDays(-7), now.AddMinutes(1), null, null), 10, default);

        Assert.Equal(new AnalyticsTotalsDto(3, 2, 1, 1, 1), raw.Totals);
        Assert.Equal(("team", 2, 2), (raw.Pages[0].Key, raw.Pages[0].Count, raw.Pages[0].Visitors));
        Assert.Equal("caudal", Assert.Single(raw.Searches).Label);
        Assert.Equal("Jornada siguiente", Assert.Single(raw.Clicks).Label);
        Assert.Equal((AnalyticsEntity.Team, team, 2, 2), Assert.Single(raw.Entities));
        Assert.Equal((club, 3, 1), Assert.Single(raw.ActorClubs));
        Assert.Equal((club, 2, 2), Assert.Single(raw.SubjectClubs));
        Assert.Equal(new ConsentStatsDto(2, 1, 1, 0, 1), raw.Consents);
        Assert.Equal(2, raw.Days.Count);

        var ofClub = await reports.ReportAsync(new AnalyticsFilter(now.AddDays(-7), now.AddMinutes(1), club, null), 10, default);
        Assert.Equal(2, ofClub.Totals.PageViews);
    }
}
