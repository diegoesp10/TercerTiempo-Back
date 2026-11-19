using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Domain.Tests;

// Un entrenamiento acabado es historia: no cambia, y la lista dice lo que fue (vino o faltó). Con eso se cuenta lo que ha
// entrenado cada jugador.
public class TrainingHistoryTests
{
    private static readonly DateTimeOffset Tomorrow = TestData.Now.AddDays(1);
    private static readonly DateTimeOffset AfterIt = Tomorrow.AddHours(3);

    private static (Squad Squad, SquadEditor Editor, SquadPlayer Pelayo, SquadPlayer Nel) SquadWithTwoPlayers()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var nel = squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, TestData.Now);
        return (squad, SquadEditor.Coach(coachId), pelayo, nel);
    }

    private static TrainingSession Plan(Squad squad, SquadEditor editor, DateTimeOffset at, int minutes = 90) =>
        TrainingSession.Plan(squad, editor, TrainingKind.Season, null, at, minutes, "Campo de La Florida", null, TestData.Now);

    [Fact]
    public void A_finished_session_cannot_be_changed_but_one_in_progress_can()
    {
        var (squad, editor, _, _) = SquadWithTwoPlayers();
        var session = Plan(squad, editor, Tomorrow);

        session.Update(TrainingKind.Recovery, null, Tomorrow, 90, null, null, Tomorrow.AddMinutes(30));
        var update = Assert.Throws<DomainException>(() => session.Update(TrainingKind.Season, null, Tomorrow, 90, null, null, AfterIt));
        var items = Assert.Throws<DomainException>(() => session.ReplaceItems([new(null, "Rondo", ExerciseType.Technical, 15, null)], AfterIt));

        Assert.Equal(ErrorCode.TrainingPastLocked, update.Code);
        Assert.Equal(ErrorCode.TrainingPastLocked, items.Code);
        Assert.Equal(TrainingKind.Recovery, session.Kind);
    }

    [Fact]
    public void Once_finished_whoever_was_expected_came_and_whoever_warned_missed()
    {
        Assert.Equal(AttendanceStatus.Attended, TrainingSession.Settled(AttendanceStatus.Expected, done: true));
        Assert.Equal(AttendanceStatus.Missed, TrainingSession.Settled(AttendanceStatus.WillMiss, done: true));
        Assert.Equal(AttendanceStatus.Missed, TrainingSession.Settled(AttendanceStatus.Missed, done: true));
        Assert.Equal(AttendanceStatus.Expected, TrainingSession.Settled(AttendanceStatus.Expected, done: false));
        Assert.Equal(AttendanceStatus.WillMiss, TrainingSession.Settled(AttendanceStatus.WillMiss, done: false));
    }

    [Fact]
    public void After_the_session_the_staff_only_marks_came_or_missed_and_keeps_who_warned()
    {
        var (squad, editor, pelayo, nel) = SquadWithTwoPlayers();
        var session = Plan(squad, editor, Tomorrow);
        session.ReportOwnAbsence(pelayo.Id, willMiss: true, "Examen", TestData.Now);

        var expected = Assert.Throws<DomainException>(() => session.SetAttendance(nel.Id, AttendanceStatus.Expected, null, AfterIt));
        var confirmed = session.SetAttendance(pelayo.Id, AttendanceStatus.Missed, "Examen de Física", AfterIt);
        var corrected = session.SetAttendance(nel.Id, AttendanceStatus.Missed, "No vino", AfterIt);

        Assert.Equal(ErrorCode.TrainingAttendanceSettled, expected.Code);
        Assert.Equal((AttendanceStatus.WillMiss, "Examen de Física"), (confirmed.Status, confirmed.Reason));
        Assert.Equal(AttendanceStatus.Missed, corrected.Status);
    }

    [Fact]
    public void The_tally_counts_finished_sessions_and_the_minutes_of_the_ones_attended()
    {
        var (squad, editor, pelayo, nel) = SquadWithTwoPlayers();
        var first = Plan(squad, editor, Tomorrow, 90);
        var second = Plan(squad, editor, Tomorrow.AddDays(2), 60);
        var third = Plan(squad, editor, Tomorrow.AddDays(4), 75);
        second.ReportOwnAbsence(pelayo.Id, willMiss: true, "Lesión", TestData.Now);
        var now = Tomorrow.AddDays(3);

        var slots = new[] { first, second, third }.Select(s => new TrainingSlot(s.Id, squad.Id, s.StartsAt, s.DurationMinutes)).ToList();
        var marks = new[] { first, second, third }
            .SelectMany(s => s.Attendance.Select(a => new AttendanceMark(s.Id, a.SquadPlayerId!.Value, a.Status)))
            .ToDictionary(m => (m.SessionId, m.SquadPlayerId), m => m.Status);

        var pelayoTally = TrainingTally.For(pelayo, squad.Id, slots, marks, now);
        var nelTally = TrainingTally.For(nel, squad.Id, slots, marks, now);

        Assert.Equal(new PlayerTrainingTally(Sessions: 2, Attended: 1, Missed: 1, Warned: 1, AttendedMinutes: 90), pelayoTally);
        Assert.Equal(new PlayerTrainingTally(Sessions: 2, Attended: 2, Missed: 0, Warned: 0, AttendedMinutes: 150), nelTally);
    }

    [Fact]
    public void A_signing_only_counts_the_sessions_after_joining_and_a_player_who_left_stops_counting()
    {
        var (squad, editor, pelayo, _) = SquadWithTwoPlayers();
        var before = Plan(squad, editor, Tomorrow);
        var joined = Tomorrow.AddDays(1);
        var signing = squad.AddPlayer("Xuan Rodríguez", Position.Defender, 4, null, joined);
        var after = Plan(squad, editor, Tomorrow.AddDays(2));
        squad.RemovePlayer(pelayo.Id, Tomorrow.AddDays(3));
        var gone = Plan(squad, editor, Tomorrow.AddDays(4));
        var now = Tomorrow.AddDays(10);

        var slots = new[] { before, after, gone }.Select(s => new TrainingSlot(s.Id, squad.Id, s.StartsAt, s.DurationMinutes)).ToList();
        var noMarks = new Dictionary<(Guid, Guid), AttendanceStatus>();

        Assert.Equal(2, TrainingTally.For(signing, squad.Id, slots, noMarks, now).Sessions);
        Assert.Equal(2, TrainingTally.For(pelayo, squad.Id, slots, noMarks, now).Sessions);
    }

    [Fact]
    public void The_sessions_missed_while_injured_count_apart_and_do_not_lower_the_rate()
    {
        var (squad, editor, pelayo, _) = SquadWithTwoPlayers();
        var sessions = Enumerable.Range(0, 4).Select(d => Plan(squad, editor, Tomorrow.AddDays(d * 2))).ToList();
        var day = DateOnly.FromDateTime(Tomorrow.UtcDateTime);
        // Lesionado en la segunda y la tercera sesión; con el alta el día de la cuarta, a esa ya le toca ir.
        var injury = PlayerInjury.Create(squad, pelayo, editor, InjuryType.MuscleTear, null, day.AddDays(1), day.AddDays(5), TestData.Now);
        injury.Close(day.AddDays(6), TestData.Now);
        var calendar = new InjuryCalendar([injury]);
        var slots = sessions.Select(s => new TrainingSlot(s.Id, squad.Id, s.StartsAt, s.DurationMinutes)).ToList();

        var tally = TrainingTally.For(pelayo, squad.Id, slots, new Dictionary<(Guid, Guid), AttendanceStatus>(), Tomorrow.AddDays(10),
            at => calendar.On(pelayo.Id, DateOnly.FromDateTime(at.UtcDateTime)) is not null);

        Assert.Equal(new PlayerTrainingTally(Sessions: 2, Attended: 2, Missed: 0, Warned: 0, AttendedMinutes: 180, Injured: 2), tally);
    }

    [Fact]
    public void Day_by_day_each_finished_session_says_what_it_was_and_they_add_up_to_the_tally()
    {
        var (squad, editor, pelayo, _) = SquadWithTwoPlayers();
        var sessions = Enumerable.Range(0, 5).Select(d => Plan(squad, editor, Tomorrow.AddDays(d * 2), 60 + d * 10)).ToList();
        sessions[1].ReportOwnAbsence(pelayo.Id, willMiss: true, "Examen", TestData.Now);
        var day = DateOnly.FromDateTime(Tomorrow.UtcDateTime);
        var injury = PlayerInjury.Create(squad, pelayo, editor, InjuryType.Contusion, null, day.AddDays(5), day.AddDays(6), TestData.Now);
        injury.Close(day.AddDays(7), TestData.Now);
        var calendar = new InjuryCalendar([injury]);
        var now = Tomorrow.AddDays(7);
        var slots = sessions.Select(s => new TrainingSlot(s.Id, squad.Id, s.StartsAt, s.DurationMinutes)).Reverse().ToList();
        var marks = new Dictionary<(Guid, Guid), AttendanceStatus>
        {
            [(sessions[1].Id, pelayo.Id)] = AttendanceStatus.WillMiss,
            [(sessions[2].Id, pelayo.Id)] = AttendanceStatus.Missed,
        };
        Func<DateTimeOffset, bool> injuredAt = at => calendar.On(pelayo.Id, DateOnly.FromDateTime(at.UtcDateTime)) is not null;

        var days = TrainingTally.Days(pelayo, squad.Id, slots, marks, now, injuredAt);

        Assert.Equal(sessions.Take(4).Select(s => s.Id), days.Select(d => d.Slot.SessionId));
        Assert.Equal([TrainingDayOutcome.Attended, TrainingDayOutcome.Warned, TrainingDayOutcome.Missed, TrainingDayOutcome.Injured], days.Select(d => d.Outcome));
        Assert.Equal(new PlayerTrainingTally(Sessions: 3, Attended: 1, Missed: 2, Warned: 1, AttendedMinutes: 60, Injured: 1), TrainingTally.Sum(days));
        Assert.Equal(TrainingTally.Sum(days), TrainingTally.For(pelayo, squad.Id, slots, marks, now, injuredAt));
    }

    [Fact]
    public void An_injury_covers_from_its_first_day_until_the_day_before_the_return()
    {
        var (squad, editor, pelayo, nel) = SquadWithTwoPlayers();
        var start = new DateOnly(2026, 11, 3);
        var old = PlayerInjury.Create(squad, pelayo, editor, InjuryType.Contusion, null, start.AddDays(-30), null, TestData.Now);
        old.Close(start.AddDays(-25), TestData.Now);
        var open = PlayerInjury.Create(squad, pelayo, editor, InjuryType.AnkleSprain, null, start, start.AddDays(21), TestData.Now);
        var calendar = new InjuryCalendar([old, open]);

        Assert.Null(calendar.On(pelayo.Id, start.AddDays(-1)));
        Assert.Same(open, calendar.On(pelayo.Id, start));
        // Sin alta sigue lesionado aunque pase la vuelta prevista.
        Assert.Same(open, calendar.On(pelayo.Id, start.AddDays(40)));
        Assert.Same(old, calendar.On(pelayo.Id, start.AddDays(-26)));
        Assert.Null(calendar.On(pelayo.Id, start.AddDays(-25)));
        Assert.Null(calendar.On(nel.Id, start));
        Assert.True(old.Overlaps(start.AddDays(-27), start.AddDays(-27)));
        Assert.False(old.Overlaps(start.AddDays(-25), null));
        Assert.True(open.Overlaps(null, start));
    }
}
