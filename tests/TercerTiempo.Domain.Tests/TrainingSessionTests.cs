using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Domain.Tests;

public class TrainingSessionTests
{
    private static readonly DateTimeOffset Tomorrow = TestData.Now.AddDays(1);

    private static (Squad Squad, SquadEditor Editor, SquadPlayer Pelayo, SquadPlayer Nel) SquadWithTwoPlayers()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var nel = squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, TestData.Now);
        return (squad, SquadEditor.Coach(coachId), pelayo, nel);
    }

    private static TrainingSession Plan(Squad squad, SquadEditor editor, TrainingKind kind = TrainingKind.Season, string? title = null, int minutes = 90) =>
        TrainingSession.Plan(squad, editor, kind, title, Tomorrow, minutes, "Campo de La Florida", null, TestData.Now);

    [Fact]
    public void Plan_counts_on_every_active_player_and_titles_the_session_by_its_kind()
    {
        var (squad, editor, pelayo, nel) = SquadWithTwoPlayers();
        squad.RemovePlayer(nel.Id, TestData.Now);

        var session = Plan(squad, editor, TrainingKind.Preseason);

        Assert.Equal("Pretemporada", session.Title);
        Assert.Equal(Tomorrow.AddMinutes(90), session.EndsAt);
        Assert.Equal(editor.CoachId, session.CreatedByCoachId);
        var line = Assert.Single(session.Attendance);
        Assert.Equal((pelayo.Id, AttendanceStatus.Expected), (line.SquadPlayerId, line.Status));
        Assert.False(session.HasStarted(TestData.Now));
        Assert.True(session.HasStarted(Tomorrow));
    }

    [Fact]
    public void Duration_and_kind_are_checked()
    {
        var (squad, editor, _, _) = SquadWithTwoPlayers();

        var tooShort = Assert.Throws<DomainException>(() => Plan(squad, editor, minutes: TrainingCatalog.MinDurationMinutes - 1));
        var tooLong = Assert.Throws<DomainException>(() => Plan(squad, editor, minutes: TrainingCatalog.MaxDurationMinutes + 1));
        var badKind = Assert.Throws<DomainException>(() => Plan(squad, editor, (TrainingKind)42));

        Assert.Equal(ErrorCode.TrainingDurationRange, tooShort.Code);
        Assert.Equal(ErrorCode.TrainingDurationRange, tooLong.Code);
        Assert.Equal(ErrorCode.TrainingInvalidKind, badKind.Code);
    }

    [Fact]
    public void A_weekly_series_takes_the_chosen_weekdays_until_the_limit_keeping_the_time()
    {
        // El lunes 19 de octubre de 2026 a las 19:30.
        var first = new DateTimeOffset(2026, 10, 19, 19, 30, 0, TimeSpan.Zero);

        var dates = TrainingSession.SeriesDates(first, new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday }, new DateOnly(2026, 10, 30), TimeZoneInfo.Utc);

        Assert.Equal([19, 21, 26, 28], dates.Select(d => d.Day));
        Assert.All(dates, d => Assert.Equal(new TimeSpan(19, 30, 0), d.TimeOfDay));
    }

    [Fact]
    public void A_series_needs_weekdays_and_has_a_ceiling()
    {
        var first = new DateTimeOffset(2026, 10, 19, 19, 30, 0, TimeSpan.Zero);
        var everyDay = Enum.GetValues<DayOfWeek>().ToHashSet();

        var none = Assert.Throws<DomainException>(() => TrainingSession.SeriesDates(first, new HashSet<DayOfWeek>(), new DateOnly(2026, 10, 30), TimeZoneInfo.Utc));
        var tooMany = Assert.Throws<DomainException>(() => TrainingSession.SeriesDates(first, everyDay, new DateOnly(2026, 10, 19).AddDays(TrainingCatalog.MaxSeriesSessions), TimeZoneInfo.Utc));

        Assert.Equal(ErrorCode.TrainingRepeatWeekdays, none.Code);
        Assert.Equal(ErrorCode.TrainingRepeatRange, tooMany.Code);
        Assert.Equal(TrainingCatalog.MaxSeriesSessions, TrainingSession.SeriesDates(first, everyDay, new DateOnly(2026, 10, 19).AddDays(TrainingCatalog.MaxSeriesSessions - 1), TimeZoneInfo.Utc).Count);
    }

    [Fact]
    public void Items_keep_their_order_and_add_up_the_planned_minutes()
    {
        var (squad, editor, _, _) = SquadWithTwoPlayers();
        var session = Plan(squad, editor);
        var libraryId = Guid.NewGuid();

        session.ReplaceItems(
        [
            new(libraryId, "Rondo", ExerciseType.Technical, 20, null),
            new(null, "Calentamiento", ExerciseType.Warmup, 15, "Con balón"),
        ], TestData.Now);

        Assert.Equal(["Rondo", "Calentamiento"], session.Items.OrderBy(i => i.Order).Select(i => i.Name));
        Assert.Equal(libraryId, session.Items.First().ExerciseId);
        Assert.Equal(35, session.PlannedMinutes);

        session.ReplaceItems([new(null, "Partidillo", ExerciseType.Game, 30, null)], TestData.Now);
        Assert.Equal(30, session.PlannedMinutes);

        var drafts = Enumerable.Range(0, TrainingCatalog.MaxItems + 1).Select(i => new TrainingItemDraft(null, $"Ejercicio {i}", ExerciseType.Other, 5, null)).ToList();
        Assert.Equal(ErrorCode.TrainingItemsTooMany, Assert.Throws<DomainException>(() => session.ReplaceItems(drafts, TestData.Now)).Code);
    }

    [Fact]
    public void An_item_needs_a_known_type_and_a_sensible_duration()
    {
        var (squad, editor, _, _) = SquadWithTwoPlayers();
        var session = Plan(squad, editor);

        var badType = Assert.Throws<DomainException>(() => session.ReplaceItems([new(null, "Raro", (ExerciseType)77, 10, null)], TestData.Now));
        var badDuration = Assert.Throws<DomainException>(() => session.ReplaceItems([new(null, "Eterno", ExerciseType.Physical, 0, null)], TestData.Now));

        Assert.Equal(ErrorCode.ExerciseInvalidType, badType.Code);
        Assert.Equal(ErrorCode.ExerciseDurationRange, badDuration.Code);
    }

    [Fact]
    public void The_staff_marks_who_missed_and_the_reason_only_stays_for_absences()
    {
        var (squad, editor, pelayo, nel) = SquadWithTwoPlayers();
        var session = Plan(squad, editor);

        var missed = session.SetAttendance(pelayo.Id, AttendanceStatus.Missed, "Sin avisar", TestData.Now);
        var attended = session.SetAttendance(nel.Id, AttendanceStatus.Attended, "no importa", TestData.Now);

        Assert.Equal((AttendanceStatus.Missed, "Sin avisar"), (missed.Status, missed.Reason));
        Assert.Equal((AttendanceStatus.Attended, (string?)null), (attended.Status, attended.Reason));
        Assert.Equal(ErrorCode.SquadPlayerNotMember, Assert.Throws<DomainException>(() => session.SetAttendance(Guid.NewGuid(), AttendanceStatus.Attended, null, TestData.Now)).Code);
        Assert.Equal(ErrorCode.TrainingAttendanceInvalid, Assert.Throws<DomainException>(() => session.SetAttendance(pelayo.Id, (AttendanceStatus)9, null, TestData.Now)).Code);
    }

    [Fact]
    public void A_player_reports_an_absence_only_before_the_session_starts()
    {
        var (squad, editor, pelayo, _) = SquadWithTwoPlayers();
        var session = Plan(squad, editor);

        var line = session.ReportOwnAbsence(pelayo.Id, true, "Trabajo", TestData.Now);
        Assert.Equal((AttendanceStatus.WillMiss, "Trabajo"), (line.Status, line.Reason));

        line = session.ReportOwnAbsence(pelayo.Id, false, null, TestData.Now);
        Assert.Equal((AttendanceStatus.Expected, (string?)null), (line.Status, line.Reason));

        var late = Assert.Throws<DomainException>(() => session.ReportOwnAbsence(pelayo.Id, true, null, Tomorrow.AddMinutes(5)));
        Assert.Equal(ErrorCode.TrainingAbsencePast, late.Code);
    }

    [Fact]
    public void A_player_added_after_planning_is_counted_once()
    {
        var (squad, editor, _, _) = SquadWithTwoPlayers();
        var session = Plan(squad, editor);
        var later = squad.AddPlayer("Xuan Menéndez", Position.Defender, 4, null, TestData.Now);

        session.EnsureAttendanceFor(squad.ActivePlayers, TestData.Now);
        session.EnsureAttendanceFor(squad.ActivePlayers, TestData.Now);

        Assert.Equal(3, session.Attendance.Count);
        Assert.Equal(AttendanceStatus.Expected, session.AttendanceOf(later.Id)!.Status);
    }
}
