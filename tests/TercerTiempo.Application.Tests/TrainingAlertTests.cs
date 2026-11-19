using System.Reflection;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Suspensions;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Grounds;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Tests;

// Entrenamientos y convocatorias: los avisos de la campanita que van al jugador (va o no va convocado) y al cuerpo
// técnico y al club (un jugador falta al entrenamiento; los partidos no llevan avisos del jugador), y los jugadores
// que vienen a prueba.
public class TrainingAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Publishing_the_call_up_tells_each_player_with_an_account_whether_he_goes()
    {
        var f = new Fixture();

        var dto = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], f.Upcoming.KickoffAt.AddHours(-2), null, "Puerta del campo", null, Publish: true), default);

        Assert.Equal(CallUpStatus.Published, dto.Status);
        // Pendientes: los convocados que aún no han contestado; el que no va solo se da por enterado.
        Assert.Equal((1, 1, 0), (dto.Counts.Called, dto.Counts.Pending, dto.Counts.Acknowledged));
        var called = Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id);
        var notCalled = Assert.Single(f.Alerts, a => a.PlayerId == f.NelAccount.Id);
        Assert.Equal((AlertKind.MatchCallUp, "/mis-convocatorias"), (called.Kind, called.Link));
        Assert.StartsWith("Vas convocado: CD Vallobín - Real Titánico", called.Title);
        Assert.Contains("Cita: ", called.Message);
        Assert.Equal(AlertKind.MatchNotCalledUp, notCalled.Kind);
        Assert.StartsWith("No vas convocado", notCalled.Title);
        // Xuan no tiene cuenta: no hay a quién avisar, pero su línea queda marcada como notificada.
        Assert.Equal(2, f.Alerts.Count);
        Assert.All(f.CallUp.Entries, e => Assert.NotNull(e.NotifiedAt));
        Assert.Equal(1, f.Saves);
    }

    [Fact]
    public async Task The_meeting_can_be_a_league_ground_with_its_map_or_a_place_written_by_hand()
    {
        var f = new Fixture();

        var dto = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], f.Upcoming.KickoffAt.AddHours(-2), f.Pixarra.Id, "Se ignora", null, Publish: true), default);

        Assert.Equal((f.Pixarra.Id, (string?)null), (dto.MeetGround?.Id, dto.MeetPlace));
        Assert.Equal(["Real Titánico"], dto.MeetGround!.HomeTeams);
        // El campo del partido: el que trae el partido («La Florida»), el habitual del equipo de casa.
        Assert.Equal(f.LaFlorida.Id, dto.MatchGround?.Id);
        Assert.Contains("La Pixarra", Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id).Message);

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() =>
            f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, Guid.NewGuid(), null, null, Publish: false), default));
        Assert.Equal(ErrorCode.GroundNotFound, unknown.Code);

        var written = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, "Campo de Otero (obras en el nuestro)", null, Publish: false), default);
        Assert.Equal(((Guid?)null, "Campo de Otero (obras en el nuestro)"), (written.MeetGround?.Id, written.MeetPlace));
    }

    [Fact]
    public async Task A_draft_tells_nobody_and_republishing_only_tells_who_changes()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: false), default);
        Assert.Empty(f.Alerts);
        Assert.Equal(CallUpStatus.Draft, f.CallUp.Status);

        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: true), default);
        Assert.Equal(2, f.Alerts.Count);

        // Nel entra en la convocatoria: su aviso anterior («no vas») se resuelve y recibe el nuevo; Pelayo no vuelve a oír nada.
        f.Clock.Advance(TimeSpan.FromMinutes(10));
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id, f.Nel.Id], null, null, null, null, Publish: false), default);

        Assert.Equal(3, f.Alerts.Count);
        var nelAlerts = f.Alerts.Where(a => a.PlayerId == f.NelAccount.Id).OrderBy(a => a.CreatedAt).ToList();
        Assert.Equal([true, false], nelAlerts.Select(a => a.IsResolved));
        Assert.Equal(AlertKind.MatchCallUp, nelAlerts[1].Kind);
        Assert.False(Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id).IsResolved);
    }

    [Fact]
    public async Task Signing_the_call_up_clears_the_player_alert_and_tells_the_staff_nothing()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: true), default);

        var mine = await f.Respond(f.PelayoUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default);

        Assert.Equal(CallUpResponse.Confirmed, mine.Mine.Response);
        Assert.True(Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id).IsResolved);
        // Los partidos no llevan avisos del jugador: la firma se ve en la convocatoria, la campanita del staff no suena.
        Assert.DoesNotContain(f.Alerts, a => a.CoachId == f.Coach.Id || a.ClubId == f.Club.Id);
        Assert.Equal(2, f.Alerts.Count);
    }

    [Fact]
    public async Task A_player_outside_the_squad_cannot_answer_and_the_not_called_one_only_acknowledges()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: true), default);

        var stranger = await Assert.ThrowsAsync<ForbiddenException>(() => f.Respond(f.StrangerUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default));
        var notCalled = await Assert.ThrowsAsync<DomainException>(() => f.Respond(f.NelUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default));
        var seen = await f.Respond(f.NelUser).Handle(new(f.CallUp.Id, CallUpResponse.Acknowledged), default);

        Assert.Equal(ErrorCode.SquadPrivate, stranger.Code);
        Assert.Equal(ErrorCode.CallUpNotCalled, notCalled.Code);
        Assert.Equal(CallUpResponse.Acknowledged, seen.Mine.Response);
        Assert.True(Assert.Single(f.Alerts, a => a.PlayerId == f.NelAccount.Id).IsResolved);
    }

    [Fact]
    public async Task The_staff_sees_who_got_the_call_up_and_who_opened_it()
    {
        var f = new Fixture();
        var sent = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: true), default);

        // Un check: les ha llegado a los dos con cuenta. Xuan no tiene cuenta: no cuenta como enviada.
        Assert.Equal((2, 0), (sent.Counts.Sent, sent.Counts.Seen));
        Assert.Null(sent.Entries.Single(e => e.SquadPlayerId == f.Xuan.Id).SentAt);

        f.Clock.Advance(TimeSpan.FromMinutes(5));
        var opened = await f.MarkSeen(f.NelUser).Handle(new(f.CallUp.Id), default);
        f.Clock.Advance(TimeSpan.FromMinutes(5));
        var again = await f.MarkSeen(f.NelUser).Handle(new(f.CallUp.Id), default);

        // Abrirla otra vez no mueve la hora de la primera; verla no es firmarla.
        Assert.Equal(Now.AddMinutes(5), opened.Mine.SeenAt);
        Assert.Equal(Now.AddMinutes(5), again.Mine.SeenAt);
        Assert.Equal(CallUpResponse.Pending, again.Mine.Response);

        // Firmar sin haberla abierto antes también cuenta como vista.
        await f.Respond(f.PelayoUser).Handle(new(f.CallUp.Id, CallUpResponse.Confirmed), default);
        var staff = await f.Resend().Handle(new(f.Squad.Id, f.Upcoming.Id), default);
        Assert.Equal((2, 2), (staff.Counts.Sent, staff.Counts.Seen));
    }

    [Fact]
    public async Task Resending_only_reaches_who_has_not_opened_it_and_a_draft_cannot_be_seen()
    {
        var f = new Fixture();
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: false), default);

        var draft = await Assert.ThrowsAsync<DomainException>(() => f.MarkSeen(f.PelayoUser).Handle(new(f.CallUp.Id), default));
        var resendDraft = await Assert.ThrowsAsync<DomainException>(() => f.Resend().Handle(new(f.Squad.Id, f.Upcoming.Id), default));
        Assert.Equal(ErrorCode.CallUpNotPublished, draft.Code);
        Assert.Equal(ErrorCode.CallUpNotPublished, resendDraft.Code);

        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: true), default);
        await f.MarkSeen(f.PelayoUser).Handle(new(f.CallUp.Id), default);
        f.Clock.Advance(TimeSpan.FromHours(1));

        await f.Resend().Handle(new(f.Squad.Id, f.Upcoming.Id), default);

        // Pelayo ya la vio: no le llega nada. Nel recibe otro aviso y el anterior se da por resuelto.
        Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id);
        var nel = f.Alerts.Where(a => a.PlayerId == f.NelAccount.Id).OrderBy(a => a.CreatedAt).ToList();
        Assert.Equal([true, false], nel.Select(a => a.IsResolved));
        Assert.Equal(AlertKind.MatchNotCalledUp, nel[1].Kind);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Resend(f.NelUser).Handle(new(f.Squad.Id, f.Upcoming.Id), default));
    }

    [Fact]
    public async Task A_weekly_plan_creates_one_session_per_date_sharing_the_series_and_the_exercises()
    {
        var f = new Fixture();
        // Lunes 19 de octubre de 2026 a las 18:00 UTC; lunes y miércoles hasta el viernes 30.
        var first = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var items = new List<TrainingItemInput> { new(null, "Calentamiento", ExerciseType.Warmup, 15, null), new(null, "Rondo", ExerciseType.Technical, 20, null) };

        var created = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, first, 90, "La Florida", null, items, [DayOfWeek.Monday, DayOfWeek.Wednesday], new DateOnly(2026, 10, 30)), default);

        Assert.Equal(4, created.Count);
        Assert.Equal(4, f.Trainings.Count);
        var seriesId = Assert.Single(f.Trainings.Select(s => s.SeriesId).Distinct());
        Assert.NotNull(seriesId);
        Assert.All(f.Trainings, s => Assert.Equal(35, s.PlannedMinutes));
        Assert.All(created, s => Assert.Equal((3, 2, "Entrenamiento de la semana"), (s.Counts.Expected, s.ItemsCount, s.Title)));
        Assert.Equal(1, f.Saves);

        var single = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Recovery, "Suave", first.AddDays(7), 60, null, null, [], [], null), default);
        Assert.Null(Assert.Single(single).SeriesId);
    }

    [Fact]
    public async Task A_player_warning_of_an_absence_alerts_the_staff_and_coming_after_all_clears_it()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);
        var session = Assert.Single(planned);

        var detail = await f.ReportAbsence(f.PelayoUser).Handle(new(session.Id, true, "Examen"), default);

        Assert.Equal((AttendanceStatus.WillMiss, "Examen"), (detail.Mine!.Status, detail.Mine.Reason));
        Assert.Equal((2, 1), (detail.Counts.Expected, detail.Counts.WillMiss));
        Assert.False(detail.CanEdit);
        var coachAlert = Assert.Single(f.Alerts, a => a.CoachId == f.Coach.Id);
        Assert.Single(f.Alerts, a => a.ClubId == f.Club.Id);
        Assert.Equal((AlertKind.TrainingAbsence, "Pelayo Fernández falta al entrenamiento"), (coachAlert.Kind, coachAlert.Title));
        Assert.Contains("de CD Vallobín. Motivo: Examen.", coachAlert.Message);
        Assert.Equal($"/plantillas/{f.Squad.Id}/agenda?sesion={session.Id}", coachAlert.Link);

        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var back = await f.ReportAbsence(f.PelayoUser).Handle(new(session.Id, false, null), default);

        Assert.Equal(AttendanceStatus.Expected, back.Mine!.Status);
        Assert.All(f.Alerts.Where(a => a.Kind == AlertKind.TrainingAbsence), a => Assert.True(a.IsResolved));
    }

    [Fact]
    public async Task A_stranger_cannot_report_an_absence_nor_save_a_call_up()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);

        var absence = await Assert.ThrowsAsync<ForbiddenException>(() => f.ReportAbsence(f.StrangerUser).Handle(new(planned[0].Id, true, null), default));
        var callUp = await Assert.ThrowsAsync<ForbiddenException>(() => f.SaveCallUp(f.NelUser).Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, true), default));

        Assert.Equal(ErrorCode.SquadPrivate, absence.Code);
        Assert.Equal(ErrorCode.SquadManagersOnly, callUp.Code);
        // Solo las citaciones a entrenar del Plan: ni ausencia ni convocatoria.
        Assert.All(f.Alerts, a => Assert.Equal(AlertKind.TrainingCall, a.Kind));
    }

    [Fact]
    public async Task A_trial_player_joins_a_session_by_name_and_is_reused_by_that_name()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [DayOfWeek.Saturday, DayOfWeek.Sunday], DateOnly.FromDateTime(Now.AddDays(8).DateTime)), default);
        var first = planned[0].Id;
        var second = planned[1].Id;

        var detail = await f.AddTrial().Handle(new(f.Squad.Id, first, null, "  Iyán Cuervo ", "Viene del Llanera"), default);
        await f.AddTrial().Handle(new(f.Squad.Id, second, null, "iyan cuervo", null), default);

        var trial = Assert.Single(f.Trials);
        Assert.Equal(("Iyán Cuervo", "Viene del Llanera", false), (trial.FullName, trial.Notes, trial.IsPromoted));
        var line = Assert.Single(detail.Attendance, a => a.Trial);
        Assert.Equal((trial.Id, "Iyán Cuervo", AttendanceStatus.Expected), (line.TrialPlayerId, line.PlayerName, line.Status));
        // Tres de la plantilla más el de prueba.
        Assert.Equal(4, detail.Counts.Expected);
        Assert.All(f.Trainings.Take(2), s => Assert.NotNull(s.TrialAttendanceOf(trial.Id)));

        // El jugador de la plantilla lo ve en la lista pero no puede pasarle lista.
        var seen = await f.ReportAbsence(f.PelayoUser).Handle(new(first, false, null), default);
        Assert.Contains(seen.Attendance, a => a.Trial && a.TrialPlayerId == trial.Id);
    }

    [Fact]
    public async Task Promoting_a_trial_player_adds_him_to_the_squad_and_keeps_his_attendance()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);
        var session = planned[0].Id;
        await f.AddTrial().Handle(new(f.Squad.Id, session, null, "Iyán Cuervo", null), default);
        var trial = f.Trials.Single();
        await f.SetAttendance().Handle(new(f.Squad.Id, session, [new(null, trial.Id, AttendanceStatus.Attended, null)]), default);

        var card = await f.Promote().Handle(new(f.Squad.Id, trial.Id, Position.Midfielder, 14), default);

        Assert.Equal(("Iyán Cuervo", Position.Midfielder, 14), (card.FullName, card.Position, card.ShirtNumber));
        Assert.Contains(f.Squad.ActivePlayers, p => p.Id == card.Id);
        Assert.Equal((card.Id, Now), (trial.PromotedSquadPlayerId, trial.PromotedAt));
        var line = f.Trainings.Single().AttendanceOf(card.Id);
        Assert.NotNull(line);
        Assert.Equal((AttendanceStatus.Attended, (Guid?)null), (line.Status, line.TrialPlayerId));
        Assert.Null(f.Trainings.Single().TrialAttendanceOf(trial.Id));

        var again = await Assert.ThrowsAsync<DomainException>(() => f.Promote().Handle(new(f.Squad.Id, trial.Id, Position.Midfielder, null), default));
        Assert.Equal(ErrorCode.TrialPlayerPromoted, again.Code);
    }

    [Fact]
    public async Task The_agenda_tells_trainings_from_matches_and_what_is_already_done()
    {
        var f = new Fixture();
        await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);
        await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: false), default);
        f.Clock.Advance(TimeSpan.FromDays(2));
        var from = DateOnly.FromDateTime(Now.AddDays(-7).DateTime);
        var to = DateOnly.FromDateTime(Now.AddDays(7).DateTime);

        var coach = await f.Agenda(f.CoachUser).Handle(new(f.Squad.Id, from, to), default);

        // En orden: el partido ya jugado, el entrenamiento ya hecho y el partido que viene, cada uno con su tipo.
        Assert.Equal(
            [(SquadEventKind.Match, true), (SquadEventKind.Training, true), (SquadEventKind.Match, false)],
            coach.Select(i => (i.Kind, i.Done)));
        Assert.All(coach.Where(i => i.Kind == SquadEventKind.Match), i => Assert.Null(i.Training));
        Assert.NotNull(coach[1].Training);
        Assert.Equal((2, 1), (coach[0].Match!.HomeGoals, coach[0].Match!.AwayGoals));
        // El borrador de la convocatoria lo ve quien la prepara; el jugador, no.
        Assert.Equal(CallUpStatus.Draft, coach[2].CallUp!.Status);
        var player = await f.Agenda(f.PelayoUser).Handle(new(f.Squad.Id, from, to), default);
        Assert.Null(player.Single(i => i.Match?.Id == f.Upcoming.Id).CallUp);
    }

    [Fact]
    public async Task Planning_a_series_cites_each_player_with_an_account_once()
    {
        var f = new Fixture();

        await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, "La Florida", null, [], [DayOfWeek.Monday, DayOfWeek.Wednesday], DateOnly.FromDateTime(Now.AddDays(20).DateTime)), default);

        // Una citación por jugador con cuenta para toda la serie (Xuan no tiene cuenta); no es una convocatoria.
        Assert.True(f.Trainings.Count > 2);
        Assert.Equal(2, f.Alerts.Count);
        Assert.All(f.Alerts, a => Assert.Equal(AlertKind.TrainingCall, a.Kind));
        var pelayo = Assert.Single(f.Alerts, a => a.PlayerId == f.PelayoAccount.Id);
        Assert.Equal($"Te citan a entrenar: {f.Trainings.Count} sesiones", pelayo.Title);
        Assert.Contains("lunes y miércoles", pelayo.Message);
        Assert.Contains("La Florida", pelayo.Message);
        Assert.StartsWith($"/plantillas/{f.Squad.Id}/agenda", pelayo.Link);
    }

    [Fact]
    public async Task Moving_or_cancelling_a_session_tells_the_players_again()
    {
        var f = new Fixture();
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default);
        var id = planned.Single().Id;

        // Cambiar solo las notas no avisa; moverla de hora, sí, y resuelve la citación anterior.
        await f.Update().Handle(new(f.Squad.Id, id, TrainingKind.Season, null, Now.AddDays(1), 90, null, "Traed peto"), default);
        Assert.Equal(2, f.Alerts.Count);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Update().Handle(new(f.Squad.Id, id, TrainingKind.Season, null, Now.AddDays(1).AddHours(1), 90, null, "Traed peto"), default);
        var pelayo = f.Alerts.Where(a => a.PlayerId == f.PelayoAccount.Id).OrderBy(a => a.CreatedAt).ToList();
        Assert.Equal([true, false], pelayo.Select(a => a.IsResolved));
        Assert.StartsWith("Entrenamiento cambiado", pelayo[1].Title);

        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Delete().Handle(new(f.Squad.Id, id), default);
        pelayo = f.Alerts.Where(a => a.PlayerId == f.PelayoAccount.Id).OrderBy(a => a.CreatedAt).ToList();
        Assert.Equal([true, true, false], pelayo.Select(a => a.IsResolved));
        Assert.StartsWith("Entrenamiento anulado", pelayo[2].Title);
        Assert.All(f.Alerts, a => Assert.Equal(AlertKind.TrainingCall, a.Kind));
    }

    [Fact]
    public async Task A_finished_session_cannot_be_deleted_and_a_series_goes_one_session_at_a_time()
    {
        var f = new Fixture();
        // Lunes 19 y miércoles 21, 26 y 28 de octubre a las 18:00 UTC.
        var first = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var planned = await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, first, 90, null, null, [], [DayOfWeek.Monday, DayOfWeek.Wednesday], new DateOnly(2026, 10, 30)), default);
        var (monday, wednesday) = (planned[0].Id, planned[1].Id);

        await f.Delete().Handle(new(f.Squad.Id, wednesday), default);
        f.Clock.Advance(TimeSpan.FromDays(4));
        var locked = await Assert.ThrowsAsync<DomainException>(() => f.Delete().Handle(new(f.Squad.Id, monday), default));

        Assert.Equal(ErrorCode.TrainingPastLocked, locked.Code);
        Assert.Equal(3, f.Trainings.Count);
        Assert.Contains(f.Trainings, s => s.Id == monday);
        Assert.DoesNotContain(f.Trainings, s => s.Id == wednesday);
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task An_injured_player_does_not_train_the_staff_sees_why_and_teammates_only_that_he_is_out()
    {
        var f = new Fixture();
        var session = (await f.Plan().Handle(new(f.Squad.Id, TrainingKind.Season, null, Now.AddDays(1), 90, null, null, [], [], null), default)).Single().Id;
        var injury = f.Injure(f.Pelayo, InjuryType.AnkleSprain, "Pisó mal en el partido", Today.AddDays(-2), Today.AddDays(19));

        var staff = await f.Get(f.CoachUser).Handle(new(f.Squad.Id, session), default);
        var line = staff.Attendance.Single(a => a.SquadPlayerId == f.Pelayo.Id);
        Assert.True(line.Injured);
        Assert.Equal(("Esguince de tobillo", "Pisó mal en el partido", Today.AddDays(19)), (line.Injury!.TypeLabel, line.Injury.Reason, line.Injury.ExpectedReturnOn));
        // No cuenta como esperado, ni vino ni faltó.
        Assert.Equal((2, 1), (staff.Counts.Expected, staff.Counts.Injured));

        // Es un dato de salud: el compañero solo ve que no está; el propio jugador ve la suya.
        var teammate = (await f.Get(f.NelUser).Handle(new(f.Squad.Id, session), default)).Attendance.Single(a => a.SquadPlayerId == f.Pelayo.Id);
        Assert.Equal((true, null), (teammate.Injured, teammate.Injury));
        Assert.NotNull((await f.Get(f.PelayoUser).Handle(new(f.Squad.Id, session), default)).Mine!.Injury);

        var marked = await Assert.ThrowsAsync<DomainException>(() =>
            f.SetAttendance().Handle(new(f.Squad.Id, session, [new(f.Pelayo.Id, null, AttendanceStatus.WillMiss, null)]), default));
        var warned = await Assert.ThrowsAsync<DomainException>(() => f.ReportAbsence(f.PelayoUser).Handle(new(session, true, null), default));
        Assert.Equal((ErrorCode.TrainingPlayerInjured, ErrorCode.TrainingPlayerInjured), (marked.Code, warned.Code));
        Assert.Empty(f.Alerts.Where(a => a.Kind == AlertKind.TrainingAbsence));

        // Con el alta antes de la sesión, vuelve a entrenar.
        injury.Close(Today.AddDays(1), Now);
        var back = await f.Get(f.CoachUser).Handle(new(f.Squad.Id, session), default);
        Assert.False(back.Attendance.Single(a => a.SquadPlayerId == f.Pelayo.Id).Injured);
        Assert.Equal((3, 0), (back.Counts.Expected, back.Counts.Injured));
    }

    [Fact]
    public async Task An_injured_player_cannot_go_called_up_and_the_staff_sees_his_injury_in_the_list()
    {
        var f = new Fixture();
        f.Injure(f.Nel, InjuryType.MuscleTear, null, Today.AddDays(-1));

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id, f.Nel.Id], null, null, null, null, Publish: false), default));
        Assert.Equal(ErrorCode.CallUpPlayerInjured, error.Code);
        Assert.Contains("Nel Suárez", error.Args.OfType<string>());

        var saved = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: true), default);
        var nel = saved.Entries.Single(e => e.SquadPlayerId == f.Nel.Id);
        Assert.Equal((false, true, "Rotura fibrilar"), (nel.Called, nel.Injured, nel.Injury!.TypeLabel));

        // Al jugador le llega su convocatoria con su lesión; a un compañero, sin el detalle.
        var own = await f.GetCallUp(f.NelUser).Handle(new(f.Squad.Id, f.Upcoming.Id), default);
        var mate = await f.GetCallUp(f.PelayoUser).Handle(new(f.Squad.Id, f.Upcoming.Id), default);
        Assert.NotNull(own.Mine!.Injury);
        Assert.Equal((true, null), (mate.Entries.Single(e => e.SquadPlayerId == f.Nel.Id).Injured, mate.Entries.Single(e => e.SquadPlayerId == f.Nel.Id).Injury));
        // Al darse por enterado también ve por qué no va.
        var answered = await f.Respond(f.NelUser).Handle(new(saved.Id!.Value, CallUpResponse.Acknowledged), default);
        Assert.Equal((true, "Rotura fibrilar"), (answered.Mine.Injured, answered.Mine.Injury?.TypeLabel));
    }

    [Fact]
    public async Task A_player_sent_off_in_the_last_match_is_suspended_for_the_next_and_cannot_go_called_up()
    {
        var f = new Fixture();
        f.Book(f.Pelayo, yellowCards: 0, redCard: true);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id, f.Nel.Id], null, null, null, null, Publish: false), default));
        Assert.Equal(ErrorCode.CallUpPlayerSuspended, error.Code);
        Assert.Equal(["Pelayo Fernández", "roja directa en la jornada 1"], error.Args.OfType<string>());

        var saved = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Nel.Id], null, null, null, null, Publish: true), default);
        var pelayo = saved.Entries.Single(e => e.SquadPlayerId == f.Pelayo.Id);
        Assert.Equal((false, true, SuspensionCause.RedCard, "Roja directa en la jornada 1"), (pelayo.Called, pelayo.Suspended, pelayo.Suspension!.Cause, pelayo.Suspension.Reason));
        Assert.False(saved.Entries.Single(e => e.SquadPlayerId == f.Nel.Id).Suspended);

        // La sanción la publica la federación: la ve el propio jugador en su convocatoria, en «Mis convocatorias» y al firmar.
        Assert.True((await f.GetCallUp(f.PelayoUser).Handle(new(f.Squad.Id, f.Upcoming.Id), default)).Mine!.Suspended);
        Assert.True((await f.MyCallUps(f.PelayoUser).Handle(new(), default)).Single().Mine.Suspended);
        var answered = await f.Respond(f.PelayoUser).Handle(new(saved.Id!.Value, CallUpResponse.Acknowledged), default);
        Assert.Equal("Roja directa en la jornada 1", answered.Mine.Suspension?.Reason);
    }

    [Fact]
    public async Task The_closed_official_sheet_has_the_last_word_on_the_cards()
    {
        var f = new Fixture();
        // En el acta interna se le apuntó la roja a Pelayo; la de la federación, ya cerrada, se la pone a Nel.
        f.Book(f.Pelayo, yellowCards: 0, redCard: true);
        f.CloseOfficialSheet(
            new OfficialLineData(MatchSide.Home, 9, "FERNANDEZ, PELAYO", true, null, null, 90, 0, 0, 0, 1, false),
            new OfficialLineData(MatchSide.Home, 1, "SUAREZ, NEL", true, null, null, 90, 0, 0, 0, 0, true));

        var saved = await f.SaveCallUp().Handle(new(f.Squad.Id, f.Upcoming.Id, [f.Pelayo.Id], null, null, null, null, Publish: false), default);

        Assert.Equal(
            [(f.Nel.Id, true, 0), (f.Pelayo.Id, false, 1), (f.Xuan.Id, false, 0)],
            saved.Entries.OrderBy(e => e.PlayerName).Select(e => (e.SquadPlayerId, e.Suspended, e.CycleYellows)));
    }

    [Fact]
    public async Task The_agenda_shows_who_cannot_play_the_next_match()
    {
        var f = new Fixture();
        f.Book(f.Pelayo, yellowCards: 0, redCard: true);
        f.Injure(f.Xuan, InjuryType.AnkleSprain, null, Today.AddDays(-1), Today.AddDays(20));
        var from = DateOnly.FromDateTime(Now.AddDays(-7).DateTime);
        var to = DateOnly.FromDateTime(Now.AddDays(7).DateTime);

        var coach = await f.Agenda(f.CoachUser).Handle(new(f.Squad.Id, from, to), default);

        // Primero los sancionados y luego los lesionados, haya convocatoria o no; lo ya jugado no lleva la lista.
        Assert.Equal(
            [(f.Pelayo.Id, UnavailabilityKind.Suspended, "Roja directa en la jornada 1"), (f.Xuan.Id, UnavailabilityKind.Injured, "Lesionado: esguince de tobillo")],
            coach.Single(i => i.Match?.Id == f.Upcoming.Id).Unavailable!.Select(u => (u.SquadPlayerId, u.Kind, u.Reason)));
        Assert.Null(coach.Single(i => i.Match?.Id == f.Played.Id).Unavailable);
        // Un compañero ve que Xuan está lesionado, pero no de qué.
        var mate = await f.Agenda(f.PelayoUser).Handle(new(f.Squad.Id, from, to), default);
        Assert.Equal("Lesionado", mate.Single(i => i.Match?.Id == f.Upcoming.Id).Unavailable!.Single(u => u.Kind == UnavailabilityKind.Injured).Reason);
    }

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new(Now);
        public Club Club { get; } = Club.Register(Guid.NewGuid(), "CD Vallobín", Now);
        public Team Team { get; } = Team.Create("CD Vallobín", "VAL", null);
        public Team Rival { get; } = Team.Create("Real Titánico", "TIT", null);
        public Coach Coach { get; } = Coach.Register(Guid.NewGuid(), "Abel Pablo", Now);
        public Player PelayoAccount { get; } = Player.Register(Guid.NewGuid(), "Pelayo Fernández", Position.Forward, null, Now);
        public Player NelAccount { get; } = Player.Register(Guid.NewGuid(), "Nel Suárez", Position.Goalkeeper, null, Now);
        public Player Stranger { get; } = Player.Register(Guid.NewGuid(), "Un desconocido", Position.Midfielder, null, Now);
        public Squad Squad { get; }
        public SquadPlayer Pelayo { get; }
        public SquadPlayer Nel { get; }
        public SquadPlayer Xuan { get; }
        public Match Upcoming { get; }
        public Match Played { get; }
        public Ground LaFlorida { get; } = Ground.Create("La Florida", Now);
        public Ground Pixarra { get; } = Ground.Create("La Pixarra", Now);
        public List<Alert> Alerts { get; } = [];
        public List<MatchCallUp> CallUps { get; } = [];
        public List<TrainingSession> Trainings { get; } = [];
        public List<TrialPlayer> Trials { get; } = [];
        public List<PlayerInjury> Injuries { get; } = [];
        public List<PlayerMatchPerformance> Performances { get; } = [];
        public List<OfficialMatchSheet> Sheets { get; } = [];
        public MatchCallUp CallUp => CallUps.Single();
        public int Saves { get; private set; }

        public ICurrentUser CoachUser => new Account(CoachId: Coach.Id, UserId: Coach.UserId, Type: AccountType.Coach);
        public ICurrentUser PelayoUser => new Account(PlayerId: PelayoAccount.Id, UserId: PelayoAccount.UserId, Type: AccountType.Player);
        public ICurrentUser NelUser => new Account(PlayerId: NelAccount.Id, UserId: NelAccount.UserId, Type: AccountType.Player);
        public ICurrentUser StrangerUser => new Account(PlayerId: Stranger.Id, UserId: Stranger.UserId, Type: AccountType.Player);

        public Fixture()
        {
            var seasonId = Guid.NewGuid();
            Club.AddTeam(Team.Id, Now);
            Squad = Squad.Create(Team.Id, seasonId, Coach.Id, Now);
            Pelayo = Squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, Now);
            Nel = Squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, Now);
            Xuan = Squad.AddPlayer("Xuan Menéndez", Position.Defender, 4, null, Now);
            Squad.LinkPlayer(Pelayo.Id, PelayoAccount.Id);
            Squad.LinkPlayer(Nel.Id, NelAccount.Id);
            Upcoming = Match.Schedule(seasonId, 2, Team.Id, Rival.Id, Now.AddDays(3), "La Florida", null);
            Played = Match.Schedule(seasonId, 1, Team.Id, Rival.Id, Now.AddDays(-4), "La Florida", null);
            Played.RecordResult(2, 1, Now.AddDays(-4).AddHours(2));
        }

        public SaveMatchCallUpHandler SaveCallUp(ICurrentUser? user = null) =>
            new(user ?? CoachUser, Squads, Coaches, Clubs, Matches, Teams, CallUpStore, Grounds, AlertStore, Unit, InjuryStore, Suspensions, Clock);

        public RespondCallUpHandler Respond(ICurrentUser user) =>
            new(user, Players, Squads, Matches, Teams, CallUpStore, Grounds, AlertStore, Unit, InjuryStore, Suspensions, Clock);

        public ListMyCallUpsHandler MyCallUps(ICurrentUser user) => new(user, Players, Squads, Matches, Teams, CallUpStore, Grounds, InjuryStore, Suspensions, Clock);

        public MarkCallUpSeenHandler MarkSeen(ICurrentUser user) => new(user, Players, Squads, Matches, Teams, CallUpStore, Grounds, Unit, Clock);

        public ResendCallUpHandler Resend(ICurrentUser? user = null) =>
            new(user ?? CoachUser, Squads, Clubs, Matches, Teams, CallUpStore, Grounds, AlertStore, Unit, InjuryStore, Suspensions, Clock);

        public GetSquadAgendaHandler Agenda(ICurrentUser user) => new(user, Squads, Clubs, TrainingStore, Matches, Teams, CallUpStore, InjuryStore, Suspensions, Clock);

        public PlanTrainingHandler Plan() => new(CoachUser, Squads, Coaches, Clubs, TrainingStore, ExerciseStore, AlertStore, Unit, Clock);

        public UpdateTrainingHandler Update() => new(CoachUser, Squads, Clubs, TrainingStore, TrialStore, AlertStore, Unit, InjuryStore, Clock);

        public DeleteTrainingHandler Delete() => new(CoachUser, Squads, Clubs, TrainingStore, AlertStore, Unit, Clock);

        public ReportTrainingAbsenceHandler ReportAbsence(ICurrentUser user) =>
            new(user, Players, Squads, Clubs, TrainingStore, TrialStore, Teams, AlertStore, Unit, InjuryStore, Clock);

        public SetAttendanceHandler SetAttendance() => new(CoachUser, Squads, Clubs, TrainingStore, TrialStore, Unit, InjuryStore, Clock);

        public AddTrialPlayerToTrainingHandler AddTrial() => new(CoachUser, Squads, Coaches, Clubs, TrainingStore, TrialStore, Unit, InjuryStore, Clock);

        public GetTrainingHandler Get(ICurrentUser user) => new(user, Squads, Clubs, TrainingStore, TrialStore, InjuryStore, Clock);

        public GetMatchCallUpHandler GetCallUp(ICurrentUser user) => new(user, Squads, Clubs, Matches, Teams, CallUpStore, Grounds, InjuryStore, Suspensions, Clock);

        // Tarjetas en el acta interna del partido ya jugado.
        public void Book(SquadPlayer player, int yellowCards, bool redCard) =>
            Performances.Add(PlayerMatchPerformance.Record(Squad, player, Played, SquadEditor.Coach(Coach.Id),
                new PerformanceStats(true, 90, 0, 0, 0, 0, 0, 0, 0, 0, yellowCards, redCard, null), Now));

        // El acta de la federación del partido ya jugado, cerrada.
        public void CloseOfficialSheet(params OfficialLineData[] lines)
        {
            var sheet = OfficialMatchSheet.Create(Played.Id, "test", "1", null, Now);
            sheet.Apply(new OfficialSheetData(null, "CD Vallobín", "Real Titánico", 2, 1, lines, []), Now);
            Sheets.Add(sheet);
        }

        private SuspensionReader Suspensions => new(Matches, PerformanceStore, SheetStore, Clock);

        private IPerformanceStore PerformanceStore => Stub<IPerformanceStore>((method, args) => method switch
        {
            nameof(IPerformanceStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<PlayerMatchPerformance>>(Performances.Where(p => p.SquadId == (Guid)args![0]!).ToList()),
            _ => throw new NotSupportedException(method),
        });

        private IOfficialSheetStore SheetStore => Stub<IOfficialSheetStore>((method, args) => method switch
        {
            nameof(IOfficialSheetStore.GetAvailableLinesByMatchIdsAsync) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>(
                Sheets.Where(s => ((IEnumerable<Guid>)args![0]!).Contains(s.MatchId)).ToList()),
            _ => throw new NotSupportedException(method),
        });

        public PlayerInjury Injure(SquadPlayer player, InjuryType type, string? reason, DateOnly from, DateOnly? expected = null)
        {
            var injury = PlayerInjury.Create(Squad, player, SquadEditor.Coach(Coach.Id), type, reason, from, expected, Now);
            Injuries.Add(injury);
            return injury;
        }

        private IInjuryStore InjuryStore => Stub<IInjuryStore>((method, args) => method switch
        {
            nameof(IInjuryStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<PlayerInjury>>(Injuries.Where(i => i.SquadId == (Guid)args![0]!).ToList()),
            _ => throw new NotSupportedException(method),
        });

        public PromoteTrialPlayerHandler Promote() => new(CoachUser, Squads, Clubs, TrialStore, TrainingStore, Unit, Clock);

        private ISquadStore Squads => Stub<ISquadStore>((method, args) => method switch
        {
            nameof(ISquadStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Squad.Id ? Squad : null),
            nameof(ISquadStore.GetByPlayerAsync) => Task.FromResult(Squad.IsMember((Guid)args![0]!) ? Squad : null),
            _ => throw new NotSupportedException(method),
        });
        private IClubStore Clubs => Stub<IClubStore>((method, args) => method switch
        {
            nameof(IClubStore.GetByTeamAsync) => Task.FromResult((Guid)args![0]! == Team.Id ? Club : null),
            nameof(IClubStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Club.UserId ? Club : null),
            nameof(IClubStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Club.Id ? Club : null),
            _ => throw new NotSupportedException(method),
        });
        private ICoachStore Coaches => Stub<ICoachStore>((method, args) => method switch
        {
            nameof(ICoachStore.GetByUserIdAsync) => Task.FromResult((Guid)args![0]! == Coach.UserId ? Coach : null),
            _ => throw new NotSupportedException(method),
        });
        private IPlayerStore Players => Stub<IPlayerStore>((method, args) => method switch
        {
            nameof(IPlayerStore.GetByUserIdAsync) => Task.FromResult(new[] { PelayoAccount, NelAccount, Stranger }.FirstOrDefault(p => p.UserId == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IMatchStore Matches => Stub<IMatchStore>((method, args) => method switch
        {
            nameof(IMatchStore.GetByIdAsync) => Task.FromResult((Guid)args![0]! == Upcoming.Id ? Upcoming : null),
            nameof(IMatchStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Match>>([Upcoming]),
            nameof(IMatchStore.GetTeamMatchesAsync) => Task.FromResult<IReadOnlyList<Match>>([Upcoming, Played]),
            _ => throw new NotSupportedException(method),
        });
        private ITeamStore Teams => Stub<ITeamStore>((method, args) => method switch
        {
            nameof(ITeamStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Team>>([Team, Rival]),
            nameof(ITeamStore.GetByIdAsync) => Task.FromResult(new[] { Team, Rival }.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ICallUpStore CallUpStore => Stub<ICallUpStore>((method, args) => method switch
        {
            nameof(ICallUpStore.GetByIdAsync) => Task.FromResult(CallUps.FirstOrDefault(c => c.Id == (Guid)args![0]!)),
            nameof(ICallUpStore.GetByMatchAsync) => Task.FromResult(CallUps.FirstOrDefault(c => c.SquadId == (Guid)args![0]! && c.MatchId == (Guid)args[1]!)),
            nameof(ICallUpStore.GetPublishedBySquadAsync) => Task.FromResult<IReadOnlyList<MatchCallUp>>(CallUps.Where(c => c.IsPublished).ToList()),
            nameof(ICallUpStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<MatchCallUp>>(CallUps.ToList()),
            nameof(ICallUpStore.GetBySquadAndMatchesAsync) => Task.FromResult<IReadOnlyList<MatchCallUp>>(CallUps
                .Where(c => ((IEnumerable<Guid>)args![1]!).Contains(c.MatchId) && (!(bool)args[2]! || c.IsPublished)).ToList()),
            nameof(ICallUpStore.Add) => Run(() => CallUps.Add((MatchCallUp)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IExerciseStore ExerciseStore => Stub<IExerciseStore>((method, args) => method switch
        {
            nameof(IExerciseStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<Exercise>>([]),
            _ => throw new NotSupportedException(method),
        });
        private ITrainingStore TrainingStore => Stub<ITrainingStore>((method, args) => method switch
        {
            nameof(ITrainingStore.GetByIdAsync) => Task.FromResult(Trainings.FirstOrDefault(s => s.Id == (Guid)args![0]!)),
            nameof(ITrainingStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<TrainingSession>>(
                Trainings.Where(s => s.SquadId == (Guid)args![0]! && s.StartsAt >= (DateTimeOffset)args[1]! && s.StartsAt < (DateTimeOffset)args[2]!).OrderBy(s => s.StartsAt).ToList()),
            nameof(ITrainingStore.GetByTrialPlayerAsync) => Task.FromResult<IReadOnlyList<TrainingSession>>(Trainings.Where(s => s.TrialAttendanceOf((Guid)args![0]!) is not null).ToList()),
            nameof(ITrainingStore.Add) => Run(() => Trainings.Add((TrainingSession)args![0]!)),
            nameof(ITrainingStore.Remove) => Run(() => Trainings.Remove((TrainingSession)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private ITrialPlayerStore TrialStore => Stub<ITrialPlayerStore>((method, args) => method switch
        {
            nameof(ITrialPlayerStore.GetByIdAsync) => Task.FromResult(Trials.FirstOrDefault(t => t.Id == (Guid)args![0]!)),
            nameof(ITrialPlayerStore.GetBySquadAsync) => Task.FromResult<IReadOnlyList<TrialPlayer>>(Trials.Where(t => t.SquadId == (Guid)args![0]!).ToList()),
            nameof(ITrialPlayerStore.Add) => Run(() => Trials.Add((TrialPlayer)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IAlertStore AlertStore => Stub<IAlertStore>((method, args) => method switch
        {
            nameof(IAlertStore.GetByClubAsync) => Task.FromResult<IReadOnlyList<Alert>>(Alerts.Where(a => a.ClubId == (Guid)args![0]!).ToList()),
            nameof(IAlertStore.GetForAsync) => Task.FromResult<IReadOnlyList<Alert>>(Alerts.Where(a => a.IsFor((Guid?)args![0], (Guid?)args[1], (Guid?)args[2])).ToList()),
            nameof(IAlertStore.GetOpenByKeyPrefixAsync) => Task.FromResult<IReadOnlyList<Alert>>(Alerts
                .Where(a => !a.IsResolved && a.Key.StartsWith((string)args![0]!, StringComparison.Ordinal))
                .Where(a => (a.PlayerId is { } p && ((IReadOnlyCollection<Guid>)args![1]!).Contains(p))
                    || (a.CoachId is { } c && ((IReadOnlyCollection<Guid>)args![2]!).Contains(c))
                    || (args![3] is Guid club && a.ClubId == club))
                .ToList()),
            nameof(IAlertStore.Add) => Run(() => Alerts.Add((Alert)args![0]!)),
            _ => throw new NotSupportedException(method),
        });
        private IGroundStore Grounds => Stub<IGroundStore>((method, args) => method switch
        {
            nameof(IGroundStore.GetBySeasonAsync) => Task.FromResult<IReadOnlyList<SeasonGround>>(
            [
                new(LaFlorida, new Dictionary<Guid, int> { [Team.Id] = 2 }),
                new(Pixarra, new Dictionary<Guid, int> { [Rival.Id] = 3 }),
            ]),
            nameof(IGroundStore.GetByIdAsync) => Task.FromResult(new[] { LaFlorida, Pixarra }.FirstOrDefault(g => g.Id == (Guid)args![0]!)),
            nameof(IGroundStore.GetByIdsAsync) => Task.FromResult<IReadOnlyList<Ground>>(new[] { LaFlorida, Pixarra }
                .Where(g => ((IEnumerable<Guid>)args![0]!).Contains(g.Id)).ToList()),
            _ => throw new NotSupportedException(method),
        });
        private IUnitOfWork Unit => Stub<IUnitOfWork>((_, _) => Task.FromResult(++Saves));

        private static object? Run(Action action) { action(); return null; }

        private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
        {
            var stub = DispatchProxy.Create<T, ClubAsStaffTests.StoreProxy>();
            ((ClubAsStaffTests.StoreProxy)(object)stub).Call = call;
            return stub;
        }
    }

    private sealed record Account(Guid? PlayerId = null, Guid? CoachId = null, Guid? ClubId = null, Guid? UserId = null, AccountType? Type = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public AccountType? AccountType => Type;
    }

    public sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
