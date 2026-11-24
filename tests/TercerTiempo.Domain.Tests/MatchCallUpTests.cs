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
        callUp.Arrange(squad, 18, called.ToHashSet(), match.KickoffAt.AddHours(-2), null, "Puerta del campo", null, TestData.Now);
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
    public void Arrange_tells_who_changes_and_wipes_their_previous_signature()
    {
        var (squad, editor, pelayo, nel, xuan) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id, nel.Id);
        callUp.Respond(pelayo.Id, CallUpResponse.Confirmed, match, TestData.Now);
        callUp.Respond(xuan.Id, CallUpResponse.Acknowledged, match, TestData.Now);

        // Nel se cae y Xuan entra; Pelayo sigue igual.
        var changed = callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id, xuan.Id }, match.KickoffAt.AddHours(-2), null, "Puerta del campo", "Llevad las dos equipaciones", TestData.Now.AddHours(1));

        Assert.Equal([nel.Id, xuan.Id], changed.Select(e => e.SquadPlayerId).OrderBy(id => Array.IndexOf([nel.Id, xuan.Id], id)));
        Assert.Equal(CallUpResponse.Confirmed, callUp.EntryOf(pelayo.Id)!.Response);
        Assert.Equal((false, CallUpResponse.Pending), (callUp.EntryOf(nel.Id)!.Called, callUp.EntryOf(nel.Id)!.Response));
        Assert.Equal((true, CallUpResponse.Pending), (callUp.EntryOf(xuan.Id)!.Called, callUp.EntryOf(xuan.Id)!.Response));
        Assert.Equal(match.KickoffAt.AddHours(-2), callUp.MeetAt);
        Assert.Equal("Llevad las dos equipaciones", callUp.Notes);
    }

    [Fact]
    public void Moving_the_meeting_of_a_sent_call_up_warns_every_called_player_again_and_keeps_their_signature()
    {
        var (squad, editor, pelayo, nel, xuan) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id, nel.Id);
        callUp.Respond(pelayo.Id, CallUpResponse.Confirmed, match, TestData.Now);
        callUp.MarkSeen(nel.Id, TestData.Now);

        var changed = callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id, nel.Id }, match.KickoffAt.AddHours(-1), null, "Puerta del campo", null, TestData.Now.AddHours(1));

        Assert.Equal(new[] { pelayo.Id, nel.Id }.Order(), changed.Select(e => e.SquadPlayerId).Order());
        Assert.DoesNotContain(changed, e => e.SquadPlayerId == xuan.Id);
        Assert.All(changed, e => Assert.Null(e.SeenAt));
        Assert.Equal(CallUpResponse.Confirmed, callUp.EntryOf(pelayo.Id)!.Response);
        Assert.Equal(2, callUp.UnseenEntries().Count(e => e.Called));
    }

    [Fact]
    public void Saving_a_sent_call_up_with_the_same_meeting_warns_nobody()
    {
        var (squad, editor, pelayo, nel, _) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id, nel.Id);

        var changed = callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id, nel.Id }, match.KickoffAt.AddHours(-2), null, "Puerta del campo", "Notas nuevas", TestData.Now.AddHours(1));

        Assert.Empty(changed);
    }

    [Fact]
    public void A_league_ground_replaces_the_written_place_and_changing_it_warns_the_called_players_again()
    {
        var (squad, editor, pelayo, nel, _) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id, nel.Id);
        callUp.MarkSeen(pelayo.Id, TestData.Now);
        callUp.MarkSeen(nel.Id, TestData.Now);
        var ground = Guid.NewGuid();

        var changed = callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id, nel.Id }, match.KickoffAt.AddHours(-2), ground, "Puerta del campo", null, TestData.Now.AddHours(1));

        Assert.Equal((ground, (string?)null), (callUp.MeetGroundId, callUp.MeetPlace));
        Assert.Equal(new[] { pelayo.Id, nel.Id }.Order(), changed.Select(e => e.SquadPlayerId).Order());

        // Mismo campo otra vez: nadie vuelve a enterarse. Volver a un sitio escrito (obras en el campo) sí avisa.
        Assert.Empty(callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id, nel.Id }, match.KickoffAt.AddHours(-2), ground, null, null, TestData.Now.AddHours(2)));
        var moved = callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id, nel.Id }, match.KickoffAt.AddHours(-2), null, "Campo de hierba artificial de al lado", null, TestData.Now.AddHours(3));
        Assert.Equal(2, moved.Count);
        Assert.Equal(((Guid?)null, "Campo de hierba artificial de al lado"), (callUp.MeetGroundId, callUp.MeetPlace));
    }

    [Fact]
    public void Publishing_needs_at_least_one_called_player_and_keeps_the_first_date()
    {
        var (squad, editor, pelayo, _, _) = SquadWithThreePlayers();
        var callUp = MatchCallUp.Draft(squad, TestData.ScheduledMatch(), editor, TestData.Now);

        Assert.Equal(ErrorCode.CallUpNoPlayers, Assert.Throws<DomainException>(() => callUp.Publish(TestData.Now)).Code);

        callUp.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id }, null, null, null, null, TestData.Now);
        callUp.Publish(TestData.Now);
        callUp.Publish(TestData.Now.AddDays(1));

        Assert.True(callUp.IsPublished);
        Assert.Equal(TestData.Now, callUp.PublishedAt);
        Assert.Single(callUp.Called);
    }

    // No hay «no puedo» para los partidos: el convocado firma y el que no va se da por enterado, nada más.
    [Fact]
    public void The_called_player_signs_and_the_rest_only_acknowledge()
    {
        var (squad, editor, pelayo, nel, _) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var callUp = Published(squad, editor, match, pelayo.Id);

        var signed = callUp.Respond(pelayo.Id, CallUpResponse.Confirmed, match, TestData.Now.AddHours(1));
        var seen = callUp.Respond(nel.Id, CallUpResponse.Acknowledged, match, TestData.Now.AddHours(1));

        Assert.Equal((CallUpResponse.Confirmed, TestData.Now.AddHours(1)), (signed.Response, signed.RespondedAt));
        Assert.Equal(CallUpResponse.Acknowledged, seen.Response);
        Assert.Equal(ErrorCode.CallUpInvalidResponse, Assert.Throws<DomainException>(() => callUp.Respond(pelayo.Id, CallUpResponse.Acknowledged, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CallUpInvalidResponse, Assert.Throws<DomainException>(() => callUp.Respond(pelayo.Id, CallUpResponse.Pending, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CallUpInvalidResponse, Assert.Throws<DomainException>(() => callUp.Respond(pelayo.Id, (CallUpResponse)3, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CallUpNotCalled, Assert.Throws<DomainException>(() => callUp.Respond(nel.Id, CallUpResponse.Confirmed, match, TestData.Now)).Code);
        Assert.Equal(ErrorCode.SquadPlayerNotMember, Assert.Throws<DomainException>(() => callUp.Respond(Guid.NewGuid(), CallUpResponse.Confirmed, match, TestData.Now)).Code);
    }

    [Fact]
    public void Signatures_only_count_on_a_published_call_up_before_kickoff()
    {
        var (squad, editor, pelayo, _, _) = SquadWithThreePlayers();
        var match = TestData.ScheduledMatch();
        var draft = MatchCallUp.Draft(squad, match, editor, TestData.Now);
        draft.Arrange(squad, 18, new HashSet<Guid> { pelayo.Id }, null, null, null, null, TestData.Now);

        Assert.Equal(ErrorCode.CallUpNotPublished, Assert.Throws<DomainException>(() => draft.Respond(pelayo.Id, CallUpResponse.Confirmed, match, TestData.Now)).Code);

        draft.Publish(TestData.Now);
        var late = Assert.Throws<DomainException>(() => draft.Respond(pelayo.Id, CallUpResponse.Confirmed, match, match.KickoffAt));
        Assert.Equal(ErrorCode.CallUpMatchStarted, late.Code);
    }

    private static (Squad Squad, SquadEditor Editor, List<SquadPlayer> Players) SquadWithNine()
    {
        var (squad, coachId) = TestData.NewSquad();
        string[] names = ["Pelayo Fernández", "Nel Suárez", "Xuan Menéndez", "Iyán García", "Telmo Álvarez", "Xurde López", "Roi Martínez", "Bruno Díaz", "Hugo Prieto"];
        var players = names.Select(n => squad.AddPlayer(n, Position.Midfielder, null, null, TestData.Now)).ToList();
        return (squad, SquadEditor.Coach(coachId), players);
    }

    [Fact]
    public void The_call_up_never_takes_more_than_its_number_and_says_how_many_are_missing()
    {
        var (squad, editor, players) = SquadWithNine();
        var callUp = MatchCallUp.Draft(squad, TestData.ScheduledMatch(), editor, TestData.Now);

        var over = Assert.Throws<DomainException>(() => callUp.Arrange(squad, 7, players.Take(8).Select(p => p.Id).ToHashSet(), null, null, null, null, TestData.Now));
        Assert.Equal(ErrorCode.CallUpOverSize, over.Code);
        Assert.Equal(new object[] { 7, 8, 1 }, over.Args);

        callUp.Arrange(squad, 7, players.Take(5).Select(p => p.Id).ToHashSet(), null, null, null, null, TestData.Now);
        Assert.Equal((7, 2), (callUp.Size, callUp.Missing(squad)));

        callUp.Arrange(squad, 7, players.Take(7).Select(p => p.Id).ToHashSet(), null, null, null, null, TestData.Now);
        Assert.Equal(0, callUp.Missing(squad));

        // Quien causa baja deja de contar: vuelve a faltar uno.
        squad.RemovePlayer(players[0].Id, TestData.Now);
        Assert.Equal(1, callUp.Missing(squad));
    }

    [Fact]
    public void A_fixed_number_in_the_squad_wins_and_without_it_the_number_has_to_be_said()
    {
        var (squad, editor, players) = SquadWithNine();
        var match = TestData.ScheduledMatch();
        var two = players.Take(2).Select(p => p.Id).ToHashSet();

        squad.SetCallUpSize(8);
        var fixedOne = MatchCallUp.Draft(squad, match, editor, TestData.Now);
        Assert.Equal(8, fixedOne.Size);
        fixedOne.Arrange(squad, 12, two, null, null, null, null, TestData.Now);
        Assert.Equal(8, fixedOne.Size);

        squad.SetCallUpSize(null);
        var asked = MatchCallUp.Draft(squad, match, editor, TestData.Now);
        Assert.Null(asked.Size);
        Assert.Equal(ErrorCode.CallUpSizeRequired, Assert.Throws<DomainException>(() => asked.Arrange(squad, null, two, null, null, null, null, TestData.Now)).Code);
        asked.Arrange(squad, 10, two, null, null, null, null, TestData.Now);
        asked.Arrange(squad, null, two, null, null, null, null, TestData.Now);
        Assert.Equal(10, asked.Size);

        Assert.Equal(ErrorCode.CallUpSizeInvalid, Assert.Throws<DomainException>(() => asked.Arrange(squad, 6, two, null, null, null, null, TestData.Now)).Code);
        Assert.Equal(ErrorCode.CallUpSizeInvalid, Assert.Throws<DomainException>(() => squad.SetCallUpSize(31)).Code);
        Assert.Equal(ErrorCode.CallUpSizeInvalid, Assert.Throws<DomainException>(() => squad.SetCallUpSize(6)).Code);
    }
}
