using System.Reflection;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Sanctions;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

public class AsturfutbolPagesTests
{
    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ catálogos

    [Fact]
    public void Reads_seasons_and_competitions_from_the_calendar_selects()
    {
        var html = Fixture("calendario.html");

        var seasons = AsturfutbolPages.ParseSelect(html, "temporada");
        var competitions = AsturfutbolPages.ParseSelect(html, "competicion");

        Assert.Equal(new CatalogEntry("22", "2026-2027"), seasons[0]);
        Assert.Contains(new CatalogEntry("22880345", "1ª Asturfútbol"), competitions);
        Assert.Contains(new CatalogEntry("22880347", "3ª Asturfútbol"), competitions);
        Assert.DoesNotContain(competitions, c => c.Code == "0");
    }

    [Fact]
    public void Reads_groups_from_the_exec_script()
    {
        var groups = AsturfutbolPages.ParseJsArray(Fixture("grupos.js"));

        Assert.Equal([new CatalogEntry("22932421", "1")], groups);
    }

    // ------------------------------------------------------------------ jornada

    [Fact]
    public void Reads_the_fixtures_of_a_matchday_with_their_sheet_codes()
    {
        var fixtures = AsturfutbolPages.ParseMatchday(Fixture("jornada-6.html"));

        Assert.Equal(10, fixtures.Count);

        var vallobin = Assert.Single(fixtures, f => f.HomeCode == "3597");
        Assert.Equal("C.D. Vallobin \"A\"", vallobin.HomeName);
        Assert.Equal("Navarro C.F. Alusin Solar \"A\"", vallobin.AwayName);
        Assert.Equal("25744780", vallobin.SheetCode);
        Assert.Equal(new DateOnly(2026, 10, 18), vallobin.Date);
        Assert.Equal(new TimeOnly(11, 30), vallobin.Time);

        var lenense = Assert.Single(fixtures, f => f.HomeCode == "4210");
        Assert.Equal("9602883", lenense.AwayCode);
        Assert.Equal(new DateOnly(2026, 10, 17), lenense.Date);
        Assert.All(fixtures, f => Assert.False(string.IsNullOrEmpty(f.SheetCode)));
    }

    [Fact]
    public void Reads_the_referee_published_under_each_fixture_of_a_matchday()
    {
        var fixtures = AsturfutbolPages.ParseMatchday(Fixture("jornada-6.html"));

        var vallobin = Assert.Single(Assert.Single(fixtures, f => f.HomeCode == "3597").Officials);
        Assert.Equal(new MatchOfficialData(OfficialRole.Referee, "Menéndez Rodríguez, Nicolás"), vallobin);

        var lenense = Assert.Single(Assert.Single(fixtures, f => f.HomeCode == "4210").Officials);
        Assert.Equal(new MatchOfficialData(OfficialRole.Referee, "Piedrafita Fernández, Javier"), lenense);

        Assert.All(fixtures, f => Assert.Equal(OfficialRole.Referee, Assert.Single(f.Officials).Role));
    }

    // ------------------------------------------------------------------ acta

    [Fact]
    public void Reads_lineups_minutes_goals_and_cards_from_a_sheet()
    {
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744780.html"), matchLength: 90)!;

        Assert.Equal("C.D. Vallobin A", sheet.HomeTeamName);
        Assert.Equal("Navarro C.F. Alusin Solar A", sheet.AwayTeamName);
        Assert.Equal("Menéndez Rodríguez, Nicolás", sheet.Referee);
        Assert.Equal(0, sheet.HomeGoals);
        Assert.Equal(5, sheet.AwayGoals);

        var home = sheet.Lines.Where(l => l.Side == MatchSide.Home).ToList();
        var away = sheet.Lines.Where(l => l.Side == MatchSide.Away).ToList();
        Assert.Equal(18, home.Count);
        Assert.Equal(18, away.Count);
        Assert.Equal(11, home.Count(l => l.Started));
        Assert.Equal(11, away.Count(l => l.Started));

        // Portero titular: 90 minutos.
        var keeper = Assert.Single(home, l => l.ShirtNumber == 1);
        Assert.Equal("CEPEDAL FERNANDEZ, JUAN", keeper.FullName);
        Assert.True(keeper.Started);
        Assert.Equal(0, keeper.MinuteIn);
        Assert.Null(keeper.MinuteOut);
        Assert.Equal(90, keeper.MinutesPlayed);

        // Sustituido en el 16': el que sale jugó 16; el que entra, 74.
        var cavia = Assert.Single(home, l => l.ShirtNumber == 11);
        Assert.Equal(16, cavia.MinuteOut);
        Assert.Equal(16, cavia.MinutesPlayed);
        var martinez = Assert.Single(home, l => l.ShirtNumber == 5);
        Assert.False(martinez.Started);
        Assert.Equal(16, martinez.MinuteIn);
        Assert.Equal(74, martinez.MinutesPlayed);

        // Suplente que no entra: sin minutos.
        var bench = Assert.Single(home, l => l.ShirtNumber == 13);
        Assert.False(bench.Started);
        Assert.Null(bench.MinuteIn);
        Assert.Equal(0, bench.MinutesPlayed);

        // Expulsado en el 48' con roja directa.
        var toral = Assert.Single(home, l => l.ShirtNumber == 8);
        Assert.True(toral.RedCard);
        Assert.Equal(48, toral.MinuteOut);
        Assert.Equal(48, toral.MinutesPlayed);

        // Amarillas.
        Assert.Equal(1, Assert.Single(home, l => l.ShirtNumber == 14).YellowCards);
        Assert.Equal(2, Assert.Single(away, l => l.ShirtNumber == 25).YellowCards);
        Assert.True(Assert.Single(away, l => l.ShirtNumber == 25).RedCard);

        // Goles atribuidos al equipo del goleador (dos de Serrano Pantiga, que entró en el 62').
        var serrano = Assert.Single(away, l => l.ShirtNumber == 7);
        Assert.Equal(2, serrano.Goals);
        Assert.Equal(62, serrano.MinuteIn);
        Assert.Equal(28, serrano.MinutesPlayed);
        Assert.Equal(1, Assert.Single(away, l => l.ShirtNumber == 9).Goals);
        Assert.Equal(0, home.Sum(l => l.Goals + l.PenaltyGoals));

        // Sucesos en orden cronológico, con el "90'+1".
        Assert.Equal(5, sheet.Events.Count(e => e.Type == OfficialEventType.Goal));
        Assert.Equal(10, sheet.Events.Count(e => e.Type == OfficialEventType.Substitution));
        Assert.Equal(5, sheet.Events.Count(e => e.Type == OfficialEventType.YellowCard));
        Assert.Equal(1, sheet.Events.Count(e => e.Type == OfficialEventType.RedCard));
        var last = sheet.Events.Last(e => e.Type == OfficialEventType.Goal);
        Assert.Equal(90, last.Minute);
        Assert.Equal(1, last.ExtraMinute);
        Assert.Equal("MARQUES FERNANDEZ, ENRIQUE", last.PlayerName);
        Assert.True(sheet.Events.Select(e => e.Minute).SequenceEqual(sheet.Events.Select(e => e.Minute).OrderBy(m => m)));

        // Tarjeta a un delegado: queda como suceso aunque no haya línea, y va a su ficha del cuerpo técnico (a la primera de
        // sus dos funciones).
        Assert.Contains(sheet.Events, e => e.Type == OfficialEventType.YellowCard && e.PlayerName == "TRELLES CAMPA, ADRIAN");
        var delegateRows = sheet.Staff!.Where(s => s.Side == MatchSide.Home && s.FullName == "TRELLES CAMPA, ADRIAN").ToList();
        Assert.Equal((1, false), (delegateRows[0].YellowCards, delegateRows[0].RedCard));
        Assert.All(delegateRows.Skip(1), s => Assert.Equal(0, s.YellowCards));
    }

    [Fact]
    public void A_card_without_minute_counts_but_does_not_cut_minutes_or_enter_the_timeline()
    {
        // La federación escribe (999') en las tarjetas sin minuto: aquí, una roja a un jugador que entró en el 77'.
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744748.html"), matchLength: 90)!;

        var sentOff = Assert.Single(sheet.Lines, l => l.FullName == "ALONSO GARCIA, LUIS");
        Assert.True(sentOff.RedCard);
        Assert.Equal(77, sentOff.MinuteIn);
        Assert.Null(sentOff.MinuteOut);
        Assert.Equal(13, sentOff.MinutesPlayed);
        Assert.All(sheet.Events, e => Assert.InRange(e.Minute, 0, 130));
        Assert.DoesNotContain(sheet.Events, e => e.PlayerName == "ALONSO GARCIA, LUIS" && e.Type == OfficialEventType.RedCard);

        // El acta entera se puede guardar.
        var stored = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "25744748", null, DateTimeOffset.UtcNow);
        stored.Apply(sheet, DateTimeOffset.UtcNow);
        Assert.Equal(OfficialSheetStatus.Available, stored.Status);
    }

    [Fact]
    public void A_sheet_the_referee_has_not_closed_has_lineups_but_no_events()
    {
        // J10 CD Vallobín 1-0 La Fresneda CF, descargada a las 15:26 del 15-11-2026: la federación ya enseña el 1-0 y las
        // alineaciones, pero la tabla de goles está vacía y no hay apartados de sustituciones ni de tarjetas.
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744818-sin-cerrar.html"), matchLength: 90)!;

        Assert.Equal("C.D. Vallobin A", sheet.HomeTeamName);
        Assert.Equal("La Fresneda A", sheet.AwayTeamName);
        Assert.Equal("Caicoya Beunza, Pablo", sheet.Referee);
        Assert.Equal(3, sheet.Officials!.Count);

        var home = sheet.Lines.Where(l => l.Side == MatchSide.Home).ToList();
        var away = sheet.Lines.Where(l => l.Side == MatchSide.Away).ToList();
        Assert.Equal(18, home.Count);
        Assert.Equal(17, away.Count);
        Assert.Equal(11, home.Count(l => l.Started));
        Assert.Equal(11, away.Count(l => l.Started));
        Assert.Contains(home, l => l.FullName == "FERNANDEZ PEÑA FERNANDEZ, MIGUEL" && l.ShirtNumber == 17);
        Assert.Contains(home, l => l.FullName == "Mateo" && l.ShirtNumber == 10);

        Assert.Empty(sheet.Events);
        Assert.All(sheet.Lines, l => Assert.Equal(0, l.Goals + l.PenaltyGoals + l.OwnGoals + l.YellowCards));
    }

    [Fact]
    public void The_same_sheet_once_closed_has_the_goal_the_changes_and_the_cards()
    {
        // La misma J10 descargada el 16-11-2026, ya cerrada por el árbitro: el marcado es el de siempre.
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744818.html"), matchLength: 90)!;

        Assert.Equal(1, sheet.HomeGoals);
        Assert.Equal(0, sheet.AwayGoals);
        var goal = Assert.Single(sheet.Events, e => e.Type == OfficialEventType.Goal);
        Assert.Equal((MatchSide.Home, 11, "GARCIA MARTINEZ, IGNACIO ALFONSO"), (goal.Side, goal.Minute, goal.PlayerName));

        Assert.Equal(8, sheet.Events.Count(e => e.Type == OfficialEventType.Substitution));
        Assert.Equal(10, sheet.Events.Count(e => e.Type == OfficialEventType.YellowCard));

        var home = sheet.Lines.Where(l => l.Side == MatchSide.Home).ToList();
        var espina = Assert.Single(home, l => l.ShirtNumber == 8);
        Assert.Equal(62, espina.MinuteIn);
        Assert.Equal(28, espina.MinutesPlayed);
        Assert.Equal(1, espina.YellowCards);
        var cendan = Assert.Single(home, l => l.ShirtNumber == 6);
        Assert.Equal(62, cendan.MinuteOut);
        Assert.Equal(62, cendan.MinutesPlayed);
        Assert.Equal(1, Assert.Single(home, l => l.ShirtNumber == 11).Goals);
        Assert.Null(Assert.Single(home, l => l.ShirtNumber == 13).MinuteIn);

        var away = sheet.Lines.Where(l => l.Side == MatchSide.Away).ToList();
        Assert.Equal(46, Assert.Single(away, l => l.ShirtNumber == 23).MinutesPlayed);
        Assert.Equal(44, Assert.Single(away, l => l.ShirtNumber == 8).MinutesPlayed);
        // Dos amarillas al segundo entrenador: suceso sin línea de jugador y, en su ficha del cuerpo técnico, expulsado.
        Assert.Equal(2, sheet.Events.Count(e => e.PlayerName == "FERNANDEZ MARTINEZ FARIZA, EMILIO"));
        var second = Assert.Single(sheet.Staff!, s => s.FullName == "FERNANDEZ MARTINEZ FARIZA, EMILIO");
        Assert.Equal((MatchSide.Away, 2, true), (second.Side, second.YellowCards, second.RedCard));
    }

    [Fact]
    public void Reads_the_coaching_staff_of_each_team_as_written_in_the_sheet()
    {
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744818.html"), matchLength: 90)!;

        var home = sheet.Staff!.Where(s => s.Side == MatchSide.Home).ToList();
        Assert.Equal(
            [
                new OfficialStaffData(MatchSide.Home, "DEL. Campo", "TRELLES CAMPA, ADRIAN"),
                new OfficialStaffData(MatchSide.Home, "DEL. Equipo", "TRELLES CAMPA, ADRIAN"),
                new OfficialStaffData(MatchSide.Home, "Entrenador", "FERNANDEZ PARDO, ABEL PABLO"),
                new OfficialStaffData(MatchSide.Home, "2ºEntrenador", "VICTORIO BALLESTERO, IGNACIO"),
                new OfficialStaffData(MatchSide.Home, "Fisioterapeuta", "GARCIA ALVAREZ, JAVIER"),
            ],
            home);

        var away = sheet.Staff!.Where(s => s.Side == MatchSide.Away).Select(s => (s.Role, s.FullName)).ToList();
        Assert.Equal(
            [("DEL. Equipo", "GONZÁLEZ MARTÍNEZ, JESÚS VICENTE"), ("Entrenador", "BOTAS GARCÍA BARRERO, PELAYO"), ("2ºEntrenador", "FERNANDEZ MARTINEZ FARIZA, EMILIO")],
            away);

        // El cuerpo técnico no se cuela entre los jugadores.
        Assert.DoesNotContain(sheet.Lines, l => l.FullName.Contains("TRELLES", StringComparison.Ordinal));
    }

    [Fact]
    public void The_staff_keeps_the_roles_the_federation_writes_and_maps_them_to_ours()
    {
        var sheet = AsturfutbolPages.ParseSheet(Fixture("acta-25744748.html"), matchLength: 90)!;
        var saved = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "25744748", null, DateTimeOffset.UtcNow);
        saved.Apply(sheet, DateTimeOffset.UtcNow);

        var away = saved.StaffOf(MatchSide.Away).ToList();
        Assert.Equal(["DEL. Equipo", "Entrenador", "Encargado Material"], away.Select(s => s.RoleLabel));
        Assert.Equal([OfficialStaffRole.TeamDelegate, OfficialStaffRole.HeadCoach, OfficialStaffRole.KitManager], away.Select(s => s.Role));
        Assert.NotNull(saved.StaffReadAt);
    }

    [Theory]
    [InlineData("Entrenador", OfficialStaffRole.HeadCoach)]
    [InlineData("2ºEntrenador", OfficialStaffRole.AssistantCoach)]
    [InlineData("2º Entrenador", OfficialStaffRole.AssistantCoach)]
    [InlineData("Segundo Entrenador", OfficialStaffRole.AssistantCoach)]
    [InlineData("Entrenador Auxiliar", OfficialStaffRole.AssistantCoach)]
    [InlineData("Entrenador Prácticas", OfficialStaffRole.AssistantCoach)]
    [InlineData("Especialista de Porteros", OfficialStaffRole.GoalkeeperCoach)]
    [InlineData("Entrenador de Porteros", OfficialStaffRole.GoalkeeperCoach)]
    [InlineData("Preparador Físico", OfficialStaffRole.FitnessCoach)]
    [InlineData("DEL. Equipo", OfficialStaffRole.TeamDelegate)]
    [InlineData("DEL. Campo", OfficialStaffRole.FieldDelegate)]
    [InlineData("Delegado", OfficialStaffRole.TeamDelegate)]
    [InlineData("Fisioterapeuta", OfficialStaffRole.Physio)]
    [InlineData("Médico", OfficialStaffRole.Medical)]
    [InlineData("Encargado Material", OfficialStaffRole.KitManager)]
    [InlineData("Utillero", OfficialStaffRole.KitManager)]
    [InlineData("Psicólogo", OfficialStaffRole.Other)]
    public void Staff_roles_from_the_federation_labels(string label, OfficialStaffRole expected) =>
        Assert.Equal(expected, OfficialStaffRoles.FromLabel(label));

    [Fact]
    public void A_team_without_a_coach_is_not_a_person_called_no_presenta()
    {
        var saved = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "1", null, DateTimeOffset.UtcNow);
        saved.ApplyStaff([
            new OfficialStaffData(MatchSide.Home, "Entrenador", "No presenta"),
            new OfficialStaffData(MatchSide.Home, "DEL. Equipo", "PEREZ GARCIA, JUAN"),
            new OfficialStaffData(MatchSide.Away, "Entrenador", "NO PRESENTADO"),
        ], DateTimeOffset.UtcNow);

        Assert.Equal(["PEREZ GARCIA, JUAN"], saved.Staff.Select(s => s.FullName));
    }

    [Fact]
    public void A_page_without_teams_is_not_a_sheet()
    {
        Assert.Null(AsturfutbolPages.ParseSheet("<html><body><p>No se ha aceptado el cookie</p></body></html>", 90));
    }

    // ------------------------------------------------------------------ sanciones del comité

    [Fact]
    public void Reads_the_matches_each_player_is_suspended_for_by_meeting_and_matchday()
    {
        var meetings = AsturfutbolPages.ParseSanctions(Fixture("sanciones-grupo-22932421.html"));

        Assert.Equal(
            [(new DateOnly(2026, 9, 15), 1), (new DateOnly(2026, 9, 29), 3), (new DateOnly(2026, 10, 16), 3), (new DateOnly(2026, 11, 17), 10)],
            meetings.Select(m => (m.MeetingOn, m.Matchday)));
        Assert.All(meetings, m => Assert.Equal("Comité Territorial de Competición", m.Committee));
        Assert.Equal(new DateOnly(2026, 9, 13), meetings[0].MatchdayOn);

        // Solo lo que cuesta partidos: ni las amonestaciones de los jugadores ni las del cuerpo técnico.
        var first = meetings[0].Sanctions;
        Assert.Equal(4, first.Count);
        Assert.All(first, s => Assert.Equal(CommitteeSanctionKind.Player, s.Kind));
        Assert.Contains(new CommitteeSanctionData("C.D. Vallobin A", "GONZALEZ CENDAN, VICTOR", "71-b", "Provocar a otro", 1), first);
        Assert.Contains(new CommitteeSanctionData("C. Europa A", "PRESTAMO GONZALEZ, PABLO", "34", "Doble amonestación", 1), first);
        Assert.Contains(new CommitteeSanctionData("Puerto Vega C.F. A", "JAVIER", "35", "Infracción de las reglas del juego determinante de expulsión", 1), first);
        Assert.DoesNotContain(first, s => s.PersonName == "NUEVO FERNANDEZ, IVAN");

        Assert.Contains(new CommitteeSanctionData("Tsk Roces A", "SANCHEZ FERNANDEZ, CESAR", "60-f", "Agredir a otro", 4), meetings[1].Sanctions);
        Assert.Equal(11, meetings[1].Sanctions.Count(s => s.Kind == CommitteeSanctionKind.Player));
        // Una reunión que vuelve sobre una jornada solo con una resolución especial: vista, sin sanciones nuevas.
        Assert.Empty(meetings[2].Sanctions);

        // La 5.ª amarilla también la sanciona el comité.
        Assert.Equal(
            ["ALVAREZ VILLAMOR, SERGIO", "MARQUES FERNANDEZ, ENRIQUE", "QUINTANA TORRES, DIEGO"],
            meetings[3].Sanctions.Where(s => s.Kind == CommitteeSanctionKind.Player).Select(s => s.PersonName).Order());
        Assert.Equal("Acumulación de amonestaciones", meetings[3].Sanctions.Single(s => s.PersonName == "QUINTANA TORRES, DIEGO").Reason);
    }

    [Fact]
    public void Reads_the_staff_sanctions_with_their_matches_or_days_of_ban()
    {
        var meetings = AsturfutbolPages.ParseSanctions(Fixture("sanciones-grupo-22932421.html"));

        // Las amonestaciones del cuerpo técnico no entran (salen de las actas); lo que cuesta partidos, sí.
        Assert.DoesNotContain(meetings[0].Sanctions, s => s.Kind == CommitteeSanctionKind.Staff);
        Assert.Equal(
            [
                new CommitteeSanctionData("Tsk Roces A", "COSTALES ALONSO, SANTIAGO", "71-f", "Protestar al árbitro", 1, CommitteeSanctionKind.Staff),
                new CommitteeSanctionData("Tsk Roces A", "VALCARCEL GARCIA, IVAN", "71-c", "Menosprecio al árbitro", 1, CommitteeSanctionKind.Staff),
            ],
            meetings[1].Sanctions.Where(s => s.Kind == CommitteeSanctionKind.Staff));

        var staff = meetings[3].Sanctions.Where(s => s.Kind == CommitteeSanctionKind.Staff).ToList();
        Assert.Equal(
            [("FERNANDEZ MARTINEZ FARIZA, EMILIO", 4, null), ("FERNANDEZ MARTINEZ FARIZA, EMILIO", 1, null), ("FERNANDEZ MARTINEZ FARIZA, EMILIO", 1, null),
             ("VERDASCO LOPEZ, PABLO", 0, 21), ("LOPEZ PRIETO, ISRAEL", 1, null)],
            staff.Select(s => (s.PersonName, s.Matches, s.Days)));
        Assert.Equal("Menosprecio al árbitro", staff.Single(s => s.Days is not null).Reason);
    }

    [Fact]
    public void A_page_without_meetings_has_no_sanctions()
    {
        Assert.Empty(AsturfutbolPages.ParseSanctions(Fixture("calendario.html")));
    }

    // ------------------------------------------------------------------ nombres

    [Theory]
    [InlineData("1ª Asturfútbol", "1ª AsturFútbol", true)]
    [InlineData("2ª Asturfútbol", "1ª AsturFútbol", false)]
    [InlineData("Tercera Federación", "3ª RFEF", true)]
    [InlineData("Tercera Federación de Fútbol Femenino", "3ª RFEF", true)]
    [InlineData("Liga Nacional Juvenil", "3ª RFEF", false)]
    public void Matches_federation_names_with_our_categories(string federation, string category, bool expected)
    {
        Assert.Equal(expected, AsturfutbolOfficialSheetProvider.Matches(federation, category));
    }

    [Theory]
    [InlineData("2026/2027", "20262027")]
    [InlineData("2026-2027", "20262027")]
    [InlineData("2026-27", "20262027")]
    public void Season_labels_compare_by_their_years(string label, string key)
    {
        Assert.Equal(key, AsturfutbolOfficialSheetProvider.SeasonKey(label));
    }
}
