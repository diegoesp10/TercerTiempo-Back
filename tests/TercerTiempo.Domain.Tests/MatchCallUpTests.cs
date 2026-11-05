using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Domain.Tests;

public class MatchCallUpTests
{
    private static (Squad Squad, SquadEditor Editor, SquadPlayer Pelayo, SquadPlayer Nel, SquadPlayer Xuan) SquadWithThreePlayers()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var nel = squad.AddPlayer("Nel Suárez", Position.Goalkeeper, 1, null, TestData.Now);
        var xuan = squad.AddPlayer("Xuan Menéndez", Position.Defender, 4, null, TestData.Now);
        return (squad, SquadEditor.Coach(coachId), pelayo, nel, xuan);
    }

    private static MatchCallUp Published(Squad squad, SquadEditor editor, Match match, params Guid[] called)
    {
        var callUp = MatchCallUp.Draft(squad, match, editor, TestData.Now);
        callUp.Arrange(squad, called.ToHashSet(), match.KickoffAt.AddHours(-2), "Puerta del campo", null, TestData.Now);
        callUp.Publish(TestData.Now);
        return callUp;
    }

    [Fact]
    public void A_draft_lists_every_active_player_with_nobody_called_yet()
    {
        var (squad, editor, _, nel, _) = SquadWithThreePlayers();
        squad.RemovePlayer(nel.Id, TestData.Now);

        var callUp = MatchCallUp.Draft(squad, TestData.ScheduledMatch(), editor, TestData.Now);

        Assert.Equal(CallUpStatus.Draft, callUp.Status);
        Assert.False(callUp.IsPublished);
        Assert.Equal(2, callUp.Entries.Count);
        Assert.Empty(callUp.Called);
        Assert.All(callUp.Entries, e => Assert.Equal(CallUpResponse.Pending, e.Response));
    }

    [Fact]
    public void No_call_up_for_a_match_already_played_or_started()
    {
        var (squad, editor, _, _, _) = SquadWithThreePlayers();
        var started = Match.Schedule(TestData.SeasonId, 3, TestData.HomeTeamId, TestData.AwayTeamId, TestData.Now.AddMinutes(-1), null, null);

        var played = Assert.Throws<DomainException>(() => MatchCallUp.Draft(squad, TestData.PlayedMatch(), editor, TestData.Now));
        var inPlay = Assert.Throws<DomainException>(() => MatchCallUp.Draft(squad, started, editor, TestData.Now));

        Assert.Equal(ErrorCode.CallUpMatchPlayed, played.Code);
        Assert.Equal(ErrorCode.CallUpMatchPlayed, inPlay.Code);
    }

    [Fact]
    public void Arrange_tells_who_changes_and_wipes_their_previous_answer()
    {
        var (squad, editor, pelayo, nel, xuan) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id, nel.Id);
        callUp.Respond(pelayo.Id, CallUpResponse.Confirmed, null, match, TestData.Now);
        callUp.Respond(xuan.Id, CallUpResponse.Acknowledged, null, match, TestData.Now);

        // Nel se cae y Xuan entra; Pelayo sigue igual.
        var changed = callUp.Arrange(squad, new HashSet<Guid> { pelayo.Id, xuan.Id }, null, null, "Llevad las dos equipaciones", TestData.Now.AddHours(1));

        Assert.Equal([nel.Id, xuan.Id], changed.Select(e => e.SquadPlayerId).OrderBy(id => Array.IndexOf([nel.Id, xuan.Id], id)));
        Assert.Equal(CallUpResponse.Confirmed, callUp.EntryOf(pelayo.Id)!.Response);
        Assert.Equal((false, CallUpResponse.Pending), (callUp.EntryOf(nel.Id)!.Called, callUp.EntryOf(nel.Id)!.Response));
        Assert.Equal((true, CallUpResponse.Pending), (callUp.EntryOf(xuan.Id)!.Called, callUp.EntryOf(xuan.Id)!.Response));
        Assert.Null(callUp.MeetAt);
        Assert.Equal("Llevad las dos equipaciones", callUp.Notes);
    }

    [Fact]
    public void Publishing_needs_at_least_one_called_player_and_keeps_the_first_date()
    {
        var (squad, editor, pelayo, _, _) = SquadWithThreePlayers();
        var callUp = MatchCallUp.Draft(squad, TestData.ScheduledMatch(), editor, TestData.Now);

        Assert.Equal(ErrorCode.CallUpNoPlayers, Assert.Throws<DomainException>(() => callUp.Publish(TestData.Now)).Code);

        callUp.Arrange(squad, new HashSet<Guid> { pelayo.Id }, null, null, null, TestData.Now);
        callUp.Publish(TestData.Now);
        callUp.Publish(TestData.Now.AddDays(1));

        Assert.True(callUp.IsPublished);
        Assert.Equal(TestData.Now, callUp.PublishedAt);
        Assert.Single(callUp.Called);
    }

    [Fact]
    public void The_called_player_confirms_or_declines_and_the_rest_only_acknowledge()
    {
        var (squad, editor, pelayo, nel, _) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id);

        var declined = callUp.Respond(pelayo.Id, CallUpResponse.Declined, "Lesionado", match, TestData.Now.AddHours(1));
        var seen = callUp.Respond(nel.Id, CallUpResponse.Acknowledged, null, match, TestData.Now.AddHours(1));

        Assert.Equal((CallUpResponse.Declined, "Lesionado", TestData.Now.AddHours(1)), (declined.Response, declined.ResponseNote, declined.RespondedAt));
        Assert.Equal(CallUpResponse.Acknowledged, seen.Response);
        Assert.Equal(ErrorCode.CallUpInvalidResponse, Assert.Throws<DomainException>(() => callUp.Respond(pelayo.Id, CallUpResponse.Acknowledged, null, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CallUpInvalidResponse, Assert.Throws<DomainException>(() => callUp.Respond(pelayo.Id, CallUpResponse.Pending, null, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CallUpNotCalled, Assert.Throws<DomainException>(() => callUp.Respond(nel.Id, CallUpResponse.Confirmed, null, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.SquadPlayerNotMember, Assert.Throws<DomainException>(() => callUp.Respond(Guid.NewGuid(), CallUpResponse.Confirmed, null, match, TestData.Now)).Code);
    }

    [Fact]
    public void Answers_only_count_on_a_published_call_up_before_kickoff()
    {
        var (squad, editor, pelayo, _, _) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var draft = MatchCallUp.Draft(squad, match, editor, TestData.Now);
        draft.Arrange(squad, new HashSet<Guid> { pelayo.Id }, null, null, null, TestData.Now);

        Assert.Equal(ErrorCode.CallUpNotPublished, Assert.Throws<DomainException>(() => draft.Respond(pelayo.Id, CallUpResponse.Confirmed, null, match, TestData.Now)).Code);

        draft.Publish(TestData.Now);
        var late = Assert.Throws<DomainException>(() => draft.Respond(pelayo.Id, CallUpResponse.Confirmed, null, match, match.KickoffAt));
        Assert.Equal(ErrorCode.CallUpMatchStarted, late.Code);
    }
}
