using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Domain.Tests;

// Dos fichas de la misma persona en una plantilla («Lucas Paredes» y «Lucas Paredes Rivera») se juntan en una: la
// repetida desaparece y lo que colgaba de ella pasa a la que se queda sin perder nada.
public class SquadPlayerMergeTests
{
    [Fact]
    public void The_kept_player_takes_what_he_lacked_and_the_duplicate_goes_away()
    {
        var (squad, _) = TestData.NewSquad();
        var keep = squad.AddPlayer("Lucas Paredes Rivera", null, null, null, TestData.Now);
        var duplicate = squad.AddPlayer("Lucas Paredes", Position.Forward, 11, null, TestData.Now);
        var account = Guid.NewGuid();
        squad.LinkPlayer(duplicate.Id, account);

        var moved = squad.MergePlayer(keep.Id, duplicate.Id);

        Assert.Equal(account, moved);
        Assert.Equal(Position.Forward, keep.Position);
        Assert.Equal(11, keep.ShirtNumber);
        Assert.Equal(keep, Assert.Single(squad.Players));
        squad.LinkPlayer(keep.Id, account);
    }

    [Fact]
    public void What_the_club_chose_for_the_kept_player_stays()
    {
        var (squad, _) = TestData.NewSquad();
        var keep = squad.AddPlayer("Lucas Paredes Rivera", Position.Midfielder, 8, null, TestData.Now);
        var duplicate = squad.AddPlayer("Lucas Paredes", Position.Forward, 11, null, TestData.Now);

        Assert.Null(squad.MergePlayer(keep.Id, duplicate.Id));

        Assert.Equal(Position.Midfielder, keep.Position);
        Assert.Equal(8, keep.ShirtNumber);
    }

    [Fact]
    public void Only_two_active_players_can_be_merged()
    {
        var (squad, _) = TestData.NewSquad();
        var keep = squad.AddPlayer("Lucas Paredes Rivera", Position.Forward, null, null, TestData.Now);
        var gone = squad.AddPlayer("Lucas Paredes", Position.Forward, null, null, TestData.Now);
        squad.RemovePlayer(gone.Id, TestData.Now);

        Assert.Throws<ArgumentException>(() => squad.MergePlayer(keep.Id, keep.Id));
        var error = Assert.Throws<DomainException>(() => squad.MergePlayer(keep.Id, gone.Id));
        Assert.Equal(ErrorCode.SquadEntryInactive, error.Code);
    }

    [Fact]
    public void The_call_up_keeps_one_line_with_what_the_duplicate_had_signed()
    {
        var (squad, coachId) = TestData.NewSquad();
        var keep = squad.AddPlayer("Lucas Paredes Rivera", Position.Forward, null, null, TestData.Now);
        var duplicate = squad.AddPlayer("Lucas Paredes", Position.Forward, null, null, TestData.Now);
        var match = TestData.ScheduledMatch();
        var callUp = MatchCallUp.Draft(squad, match, SquadEditor.Coach(coachId), TestData.Now);
        callUp.Arrange(squad, 18, new HashSet<Guid> { duplicate.Id }, match.KickoffAt.AddHours(-2), null, "Puerta del campo", null, TestData.Now);
        callUp.Publish(TestData.Now);
        callUp.Respond(duplicate.Id, CallUpResponse.Confirmed, match, TestData.Now);

        callUp.MergeEntries(duplicate.Id, keep.Id);

        var line = Assert.Single(callUp.Entries);
        Assert.Equal(keep.Id, line.SquadPlayerId);
        Assert.True(line.Called);
        Assert.Equal(CallUpResponse.Confirmed, line.Response);
        Assert.NotNull(line.SeenAt);
    }

    [Fact]
    public void A_call_up_where_only_the_duplicate_was_listed_moves_to_the_kept_player()
    {
        var (squad, coachId) = TestData.NewSquad();
        var duplicate = squad.AddPlayer("Lucas Paredes", Position.Forward, null, null, TestData.Now);
        var match = TestData.ScheduledMatch();
        var callUp = MatchCallUp.Draft(squad, match, SquadEditor.Coach(coachId), TestData.Now);
        var keep = squad.AddPlayer("Lucas Paredes Rivera", Position.Forward, null, null, TestData.Now);

        callUp.MergeEntries(duplicate.Id, keep.Id);

        Assert.Equal(keep.Id, Assert.Single(callUp.Entries).SquadPlayerId);
    }

    [Fact]
    public void Training_attendance_keeps_what_was_noted_for_either_of_them()
    {
        var (squad, coachId) = TestData.NewSquad();
        var keep = squad.AddPlayer("Lucas Paredes Rivera", Position.Forward, null, null, TestData.Now);
        var duplicate = squad.AddPlayer("Lucas Paredes", Position.Forward, null, null, TestData.Now);
        var editor = SquadEditor.Coach(coachId);
        var both = TrainingSession.Plan(squad, editor, TrainingKind.Season, null, TestData.Now.AddDays(-2), 90, null, null, TestData.Now.AddDays(-3));
        both.SetAttendance(duplicate.Id, AttendanceStatus.Missed, "Lesión", TestData.Now);
        var keptSaidMore = TrainingSession.Plan(squad, editor, TrainingKind.Season, null, TestData.Now.AddDays(-1), 90, null, null, TestData.Now.AddDays(-3));
        keptSaidMore.SetAttendance(keep.Id, AttendanceStatus.Missed, "Viaje", TestData.Now);

        both.MergeAttendance(duplicate.Id, keep.Id, TestData.Now);
        keptSaidMore.MergeAttendance(duplicate.Id, keep.Id, TestData.Now);

        // Una sesión acabada da por venido a quien se esperaba: lo anotado a mano (faltó) es lo que cuenta.
        var line = Assert.Single(both.Attendance);
        Assert.Equal((keep.Id, AttendanceStatus.Missed, "Lesión"), (line.SquadPlayerId!.Value, line.Status, line.Reason));
        Assert.Equal("Viaje", Assert.Single(keptSaidMore.Attendance).Reason);
    }

    [Fact]
    public void A_session_where_only_the_duplicate_was_listed_moves_to_the_kept_player()
    {
        var (squad, coachId) = TestData.NewSquad();
        var duplicate = squad.AddPlayer("Lucas Paredes", Position.Forward, null, null, TestData.Now);
        var session = TrainingSession.Plan(squad, SquadEditor.Coach(coachId), TrainingKind.Season, null, TestData.Now.AddDays(-1), 90, null, null, TestData.Now.AddDays(-3));
        session.SetAttendance(duplicate.Id, AttendanceStatus.Missed, null, TestData.Now);
        var keep = squad.AddPlayer("Lucas Paredes Rivera", Position.Forward, null, null, TestData.Now);

        session.MergeAttendance(duplicate.Id, keep.Id, TestData.Now);

        var line = Assert.Single(session.Attendance);
        Assert.Equal(keep.Id, line.SquadPlayerId);
        Assert.Equal(AttendanceStatus.Missed, line.Status);
    }
}
