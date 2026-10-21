using TercerTiempo.Application.Abstractions.FootballData;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Infrastructure.FootballData.FutbolAsturiano;

namespace TercerTiempo.Application.Tests;

/// <summary>
/// Lectura de las páginas de partido de futbolasturiano.es y plan de recuperación de los partidos que su API oculta.
/// Los HTML son fragmentos reales de la web (octubre de 2026) recortados a lo que se usa.
/// </summary>
public class FutbolAsturianoScrapingTests
{
    // Partido de la jornada 6 de 1ª AsturFútbol todavía "programado" en WordPress: la API REST no lo devuelve.
    private const string ScheduledEventHtml = """
        <html><head><title>SD Lenense vs Real Sporting &#8216;C&#8217; - FutbolAsturiano.es</title></head><body>
        <h1>SD Lenense vs Real Sporting &#8216;C&#8217;</h1>
        <div class="sp-template sp-template-event-logos sp-template-event-logos-inline"><div class="sp-event-logos sp-event-logos-2">
          <span class="sp-team-logo"><a href="https://www.futbolasturiano.es/equipo/sd-lenense/"><strong class="sp-team-name">SD Lenense</strong> <img src="x.png" /></a></span>
          vs
          <span class="sp-team-logo"><a href="https://www.futbolasturiano.es/equipo/sporting-c/"><img src="y.png" /> <strong class="sp-team-name">Real Sporting &#8216;C&#8217;</strong></a></span>
        </div></div>
        <div class="sportspress"><div class="sp-template sp-template-event-details"><h4 class="sp-table-caption">Detalles</h4>
          <table class="sp-event-details sp-data-table sp-scrollable-table">
            <thead><tr><th>Fecha</th><th>Hora</th><th>Liga</th><th>Temporada</th><th>Jornada</th><th>Tiempo completo</th></tr></thead>
            <tbody><tr class="odd"><td>17/10/2026</td><td>18:00</td><td>1ª AsturFútbol</td><td>2026/2027</td><td>6</td><td>90'</td></tr></tbody>
          </table>
        </div></div>
        <p class="fa-partido-estadio">Estadio: <a href="https://www.futbolasturiano.es/estadio/el-soton/">El Sotón</a></p>
        </body></html>
        """;

    // Partido ya disputado: el marcador aparece junto a cada escudo.
    private const string PlayedEventHtml = """
        <div class="sp-template sp-template-event-logos sp-template-event-logos-inline"><div class="sp-event-logos sp-event-logos-2">
          <span class="sp-team-logo"><a href="https://www.futbolasturiano.es/equipo/ud-san-claudio/"><strong class="sp-team-name">UD San Claudio</strong> </a> <strong class="sp-team-result">1</strong></span>
          vs
          <span class="sp-team-logo"><strong class="sp-team-result">2</strong> <a href="https://www.futbolasturiano.es/equipo/real-aviles-industrial-b/"> <strong class="sp-team-name">Real Avilés Industrial &#8216;B&#8217;</strong></a></span>
        </div></div>
        <table class="sp-event-details sp-data-table sp-scrollable-table">
          <thead><tr><th>Fecha</th><th>Hora</th><th>Liga</th><th>Temporada</th><th>Jornada</th><th>Tiempo completo</th></tr></thead>
          <tbody><tr class="odd"><td>09/10/2026</td><td>20:00</td><td>1ª AsturFútbol</td><td>2026/2027</td><td>5</td><td>90'</td></tr></tbody>
        </table>
        <p class="fa-partido-estadio">Estadio: <a href="https://www.futbolasturiano.es/estadio/jose-ramon-suarez-fernandez/">José Ramón Suárez Fernández</a></p>
        """;

    private static readonly TimeZoneInfo Madrid = SiteClock.Resolve("Europe/Madrid");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ página de partido

    [Fact]
    public void Parses_a_scheduled_event_page()
    {
        var page = EventPageParser.Parse(ScheduledEventHtml);

        Assert.True(page.IsMatch);
        Assert.False(page.Played);
        Assert.Equal(6, page.Matchday);
        Assert.Equal(new DateOnly(2026, 10, 17), page.Date);
        Assert.Equal(new TimeOnly(18, 0), page.Time);
        Assert.Equal("sd-lenense", page.HomeTeamSlug);
        Assert.Equal("sporting-c", page.AwayTeamSlug);
        Assert.Equal("El Sotón", page.Venue);
        Assert.True(page.BelongsTo("1ª AsturFútbol", "2026/2027"));
        Assert.False(page.BelongsTo("2ª AsturFútbol - Grupo 1", "2026/2027"));
        Assert.False(page.BelongsTo("1ª AsturFútbol", "2025/2026"));
    }

    [Fact]
    public void Parses_the_score_of_a_played_event_page()
    {
        var page = EventPageParser.Parse(PlayedEventHtml);

        Assert.True(page.Played);
        Assert.False(page.Live);
        Assert.Equal(1, page.HomeGoals);
        Assert.Equal(2, page.AwayGoals);
        Assert.Equal("ud-san-claudio", page.HomeTeamSlug);
        Assert.Equal("real-aviles-industrial-b", page.AwayTeamSlug);
        Assert.Equal("José Ramón Suárez Fernández", page.Venue);
    }

    [Fact]
    public void A_match_in_progress_is_not_taken_as_finished()
    {
        // Durante el partido la web pinta el marcador en directo (0-0) y la etiqueta "EN DIRECTO".
        var live = PlayedEventHtml
            .Replace("sp-team-result\">1", "sp-team-result sp-team-result--directo\">0")
            .Replace("sp-team-result\">2", "sp-team-result sp-team-result--directo\">0")
            .Replace("</div></div>", "</div><span class=\"fa-en-directo-badge\">● EN DIRECTO</span></div>");

        var page = EventPageParser.Parse(live);

        Assert.True(page.Live);
        Assert.False(page.Played);
        Assert.Equal(0, page.HomeGoals);
    }

    [Fact]
    public void League_names_match_ignoring_entities_accents_and_separators()
    {
        var page = EventPageParser.Parse(ScheduledEventHtml.Replace("1ª AsturFútbol", "3ª AsturFútbol - Grupo 1"));

        Assert.True(page.BelongsTo("3ª AsturFútbol &#8211; Grupo 1", "2026/2027"));
    }

    [Fact]
    public void A_page_that_is_not_a_match_is_not_recognised()
    {
        var page = EventPageParser.Parse("<html><body><h1>Sanciones 14-10-2026</h1><p>Noticia</p></body></html>");

        Assert.False(page.IsMatch);
    }

    [Fact]
    public void Missing_kickoff_time_defaults_to_noon()
    {
        var page = EventPageParser.Parse(ScheduledEventHtml.Replace("<td>18:00</td>", "<td></td>"));

        Assert.Null(page.Time);
        Assert.Equal(new DateTime(2026, 10, 17, 12, 0, 0), page.LocalKickoff);
    }

    // ------------------------------------------------------------------ hora del sitio

    [Fact]
    public void Site_local_times_are_converted_to_utc_with_daylight_saving()
    {
        // Horario de verano (UTC+2) hasta el 25 de octubre de 2026; después, UTC+1.
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero), SiteClock.ToUtc(new DateTime(2026, 10, 17, 18, 0, 0), Madrid));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 11, 0, 0, TimeSpan.Zero), SiteClock.ToUtc(new DateTime(2026, 11, 1, 12, 0, 0), Madrid));
    }

    [Fact]
    public void Nonexistent_spring_forward_times_are_shifted_forward()
    {
        // 29-03-2026 02:30 no existe en Madrid (de 02:00 se pasa a 03:00).
        var utc = SiteClock.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), Madrid);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), utc);
    }

    // ------------------------------------------------------------------ plan de recuperación

    [Fact]
    public void Plan_targets_the_gaps_of_the_league_block_starting_after_the_last_played_match()
    {
        // Bloque de 10 ids (100-109). Jugados 100-103; ocultos 104, 105 (jornada en curso) y 108; futuros visibles 106, 107, 109.
        var visible = Visible(played: [100, 101, 102, 103], upcoming: [106, 107, 109]);

        var plan = HiddenEventPlanner.Plan(visible, total: 10, known: [], Now, TimeSpan.FromDays(7), TimeSpan.FromDays(2));

        Assert.Equal(3, plan.Hidden);
        Assert.Equal([104, 105, 108], plan.Unknown);
        Assert.Empty(plan.Refresh);
        Assert.Empty(plan.Reused);
        Assert.Equal(110, plan.NextId);
    }

    [Fact]
    public void Plan_extends_past_the_last_visible_id_when_the_rest_of_the_calendar_is_hidden()
    {
        // Solo se ven las 2 primeras jornadas (ids 200-203) de un calendario de 8 partidos (200-207).
        var visible = Visible(played: [200, 201, 202, 203], upcoming: []);

        var plan = HiddenEventPlanner.Plan(visible, total: 8, known: [], Now, TimeSpan.FromDays(7), TimeSpan.FromDays(2));

        Assert.Equal([204, 205, 206, 207], plan.Unknown);
    }

    [Fact]
    public void Plan_follows_the_first_half_series_when_home_and_away_rounds_are_interleaved()
    {
        // Como en 3ª AsturFútbol: ida en ids impares y vuelta en pares. Se ven las jornadas jugadas de la ida
        // (impares 301-311); la vuelta de esas jornadas (pares 302-310) y todo lo demás está oculto.
        var visible = Visible(played: [301, 303, 305, 307, 309, 311], upcoming: []);

        var plan = HiddenEventPlanner.Plan(visible, total: 20, known: [], Now, TimeSpan.FromDays(7), TimeSpan.FromDays(2));

        // Primero la ida que sigue (impares tras 311), luego el resto de menor a mayor.
        Assert.Equal([313, 315, 317, 319, 302, 304, 306, 308, 310, 312, 314, 316, 318, 320], plan.Unknown);
    }

    [Fact]
    public void Plan_reuses_known_far_matches_and_refreshes_the_ones_played_soon()
    {
        var visible = Visible(played: [300, 301], upcoming: [304]);
        var soon = Known("302", Now.AddDays(1));          // este fin de semana: se vuelve a leer
        var far = Known("303", Now.AddDays(40));          // dentro de mes y pico: se reutiliza
        var stale = Known("999", Now.AddDays(3));         // fuera del bloque: se ignora

        var plan = HiddenEventPlanner.Plan(visible, total: 5, known: [soon, far, stale], Now, TimeSpan.FromDays(7), TimeSpan.FromDays(2));

        Assert.Equal(2, plan.Hidden);
        Assert.Equal([302], plan.Refresh);
        Assert.Equal([far], plan.Reused);
        Assert.Empty(plan.Unknown);
    }

    [Fact]
    public void Plan_is_empty_when_nothing_is_hidden()
    {
        var plan = HiddenEventPlanner.Plan(Visible(played: [1, 2, 3], upcoming: []), total: 3, known: [], Now, TimeSpan.FromDays(7), TimeSpan.FromDays(2));

        Assert.Equal(0, plan.Hidden);
        Assert.Empty(plan.Unknown);
    }

    private static List<VisibleEvent> Visible(int[] played, int[] upcoming) =>
    [
        .. played.Select(id => new VisibleEvent(id, Now.AddDays(-7))),
        .. upcoming.Select(id => new VisibleEvent(id, Now.AddDays(14))),
    ];

    private static MatchSnapshot Known(string id, DateTimeOffset kickoff) =>
        new(id, 6, "1", "2", kickoff, MatchStatus.Scheduled, null, null, null);
}
