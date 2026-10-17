using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

public class SquadTests
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // ------------------------------------------------------------ creación y cuerpo técnico

    [Fact]
    public void Create_starts_private_with_invite_code_and_head_coach()
    {
        var (squad, coachId) = TestData.NewSquad();

        Assert.Equal(TestData.HomeTeamId, squad.TeamId);
        Assert.Equal(TestData.SeasonId, squad.SeasonId);
        Assert.Equal(SquadVisibility.Private, squad.Visibility);
        Assert.Equal(InviteCodes.Length, squad.InviteCode.Length);
        Assert.All(squad.InviteCode, c => Assert.Contains(c, Alphabet));
        Assert.True(squad.IsStaff(coachId));
        Assert.True(squad.IsHeadCoach(coachId));
        Assert.Single(squad.ActiveStaff);
        Assert.Empty(squad.Players);
    }

    [Fact]
    public void Same_coach_cannot_join_staff_twice()
    {
        var (squad, coachId) = TestData.NewSquad();

        Assert.Throws<DomainException>(() => squad.AddStaff(coachId, StaffRole.AssistantCoach, TestData.Now));
    }

    [Fact]
    public void Last_head_coach_cannot_leave()
    {
        var (squad, coachId) = TestData.NewSquad();
        squad.AddStaff(Guid.NewGuid(), StaffRole.AssistantCoach, TestData.Now);

        Assert.Throws<DomainException>(() => squad.RemoveStaff(coachId, TestData.Now));
    }

    [Fact]
    public void Assistant_can_leave_and_stops_being_staff()
    {
        var (squad, _) = TestData.NewSquad();
        var assistant = Guid.NewGuid();
        squad.AddStaff(assistant, StaffRole.AssistantCoach, TestData.Now);

        squad.RemoveStaff(assistant, TestData.Now.AddDays(1));

        Assert.False(squad.IsStaff(assistant));
        Assert.Equal(2, squad.Staff.Count);
        Assert.Single(squad.ActiveStaff);
        Assert.Throws<DomainException>(() => squad.RemoveStaff(assistant, TestData.Now.AddDays(2)));
    }

    [Fact]
    public void Head_coach_can_leave_when_another_head_coach_remains()
    {
        var (squad, coachId) = TestData.NewSquad();
        squad.AddStaff(Guid.NewGuid(), StaffRole.HeadCoach, TestData.Now);

        squad.RemoveStaff(coachId, TestData.Now);

        Assert.False(squad.IsStaff(coachId));
    }

    // ------------------------------------------------------------ fichas

    [Fact]
    public void AddPlayer_rejects_duplicate_names_ignoring_case_and_accents()
    {
        var (squad, _) = TestData.NewSquad();
        squad.AddPlayer("José Ángel Muñiz", Position.Midfielder, 8, null, TestData.Now);

        Assert.Throws<DomainException>(() => squad.AddPlayer("  jose angel MUNIZ ", null, null, null, TestData.Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Shirt_number_must_be_between_1_and_99(int shirtNumber)
    {
        var (squad, _) = TestData.NewSquad();

        Assert.Throws<DomainException>(() => squad.AddPlayer("Pelayo", Position.Forward, shirtNumber, null, TestData.Now));
    }

    [Fact]
    public void Importing_the_same_roster_entry_twice_updates_and_reactivates_it()
    {
        var (squad, _) = TestData.NewSquad();
        var rosterEntryId = Guid.NewGuid();
        var entry = squad.AddPlayer("Pelayo Fernández", null, 7, rosterEntryId, TestData.Now);
        squad.RemovePlayer(entry.Id, TestData.Now);

        var again = squad.AddPlayer("Pelayo Fernández", Position.Defender, null, rosterEntryId, TestData.Now.AddDays(1));

        Assert.Same(entry, again);
        Assert.Single(squad.Players);
        Assert.True(again.IsActive);
        Assert.Equal(Position.Defender, again.Position);
        Assert.Equal(7, again.ShirtNumber);
    }

    [Fact]
    public void RemovePlayer_marks_the_entry_as_left_but_keeps_it()
    {
        var (squad, _) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);

        squad.RemovePlayer(entry.Id, TestData.Now.AddDays(1));

        Assert.Equal(SquadPlayerStatus.Left, entry.Status);
        Assert.Equal(TestData.Now.AddDays(1), entry.LeftAt);
        Assert.Single(squad.Players);
        Assert.Empty(squad.ActivePlayers);
        Assert.Throws<DomainException>(() => squad.RemovePlayer(entry.Id, TestData.Now.AddDays(2)));
    }

    [Fact]
    public void UpdatePlayer_rejects_a_name_already_used_by_another_entry()
    {
        var (squad, _) = TestData.NewSquad();
        squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var other = squad.AddPlayer("Nel", Position.Defender, 4, null, TestData.Now);

        Assert.Throws<DomainException>(() => squad.UpdatePlayer(other.Id, "PELAYO", Position.Defender, 4));

        squad.UpdatePlayer(other.Id, "Nel Suárez", Position.Midfielder, 6);
        Assert.Equal("Nel Suárez", other.FullName);
        Assert.Equal(6, other.ShirtNumber);
    }

    [Fact]
    public void GetPlayer_fails_for_entries_of_other_squads()
    {
        var (squad, _) = TestData.NewSquad();

        Assert.Throws<DomainException>(() => squad.GetPlayer(Guid.NewGuid()));
    }

    // ------------------------------------------------------------ vínculo con cuentas de jugador

    [Fact]
    public void LinkPlayer_binds_an_account_to_its_entry()
    {
        var (squad, _) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var playerId = Guid.NewGuid();

        var linked = squad.LinkPlayer(entry.Id, playerId);

        Assert.Same(entry, linked);
        Assert.True(entry.IsLinked);
        Assert.True(squad.IsMember(playerId));
        Assert.Same(entry, squad.PlayerEntryOf(playerId));
    }

    [Fact]
    public void Entry_linked_to_another_account_cannot_be_taken()
    {
        var (squad, _) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        squad.LinkPlayer(entry.Id, Guid.NewGuid());

        Assert.Throws<DomainException>(() => squad.LinkPlayer(entry.Id, Guid.NewGuid()));
    }

    [Fact]
    public void An_account_cannot_be_linked_to_two_entries_of_the_same_squad()
    {
        var (squad, _) = TestData.NewSquad();
        var first = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var second = squad.AddPlayer("Nel", Position.Defender, 4, null, TestData.Now);
        var playerId = Guid.NewGuid();
        squad.LinkPlayer(first.Id, playerId);

        Assert.Throws<DomainException>(() => squad.LinkPlayer(second.Id, playerId));
    }

    [Fact]
    public void Inactive_entries_cannot_be_linked()
    {
        var (squad, _) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        squad.RemovePlayer(entry.Id, TestData.Now);

        Assert.Throws<DomainException>(() => squad.LinkPlayer(entry.Id, Guid.NewGuid()));
    }

    [Fact]
    public void UnlinkPlayer_frees_the_entry()
    {
        var (squad, _) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var playerId = Guid.NewGuid();
        squad.LinkPlayer(entry.Id, playerId);

        squad.UnlinkPlayer(playerId);

        Assert.False(entry.IsLinked);
        Assert.False(squad.IsMember(playerId));
        Assert.True(entry.IsActive);
        Assert.Throws<DomainException>(() => squad.UnlinkPlayer(playerId));
    }

    // ------------------------------------------------------------ visibilidad e invitaciones

    [Fact]
    public void Private_squad_is_visible_only_to_its_staff_and_linked_players()
    {
        var (squad, coachId) = TestData.NewSquad();
        var entry = squad.AddPlayer("Pelayo", Position.Forward, 9, null, TestData.Now);
        var playerId = Guid.NewGuid();
        squad.LinkPlayer(entry.Id, playerId);

        Assert.False(squad.CanBeViewedBy(null, null));
        Assert.False(squad.CanBeViewedBy(Guid.NewGuid(), null));
        Assert.False(squad.CanBeViewedBy(null, Guid.NewGuid()));
        Assert.True(squad.CanBeViewedBy(coachId, null));
        Assert.True(squad.CanBeViewedBy(null, playerId));
    }

    [Fact]
    public void Public_squad_is_visible_to_anyone()
    {
        var (squad, _) = TestData.NewSquad();

        squad.SetVisibility(SquadVisibility.Public);

        Assert.True(squad.CanBeViewedBy(null, null));
        Assert.True(squad.CanBeViewedBy(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void SetVisibility_rejects_unknown_values()
    {
        var (squad, _) = TestData.NewSquad();

        Assert.Throws<DomainException>(() => squad.SetVisibility((SquadVisibility)7));
    }

    [Fact]
    public void RotateInviteCode_replaces_the_code()
    {
        var (squad, _) = TestData.NewSquad();
        var before = squad.InviteCode;

        squad.RotateInviteCode();

        Assert.NotEqual(before, squad.InviteCode);
        Assert.Equal(InviteCodes.Length, squad.InviteCode.Length);
    }

    [Theory]
    [InlineData(" ab-cd ef ", "ABCDEF")]
    [InlineData("xyz2 345", "XYZ2345")]
    [InlineData(null, "")]
    public void Invite_codes_are_normalized_before_lookup(string? raw, string expected)
    {
        Assert.Equal(expected, InviteCodes.Normalize(raw));
    }

    [Fact]
    public void Generated_invite_codes_avoid_ambiguous_characters()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => InviteCodes.Generate()).ToList();

        Assert.All(codes, code =>
        {
            Assert.Equal(InviteCodes.Length, code.Length);
            Assert.All(code, c => Assert.Contains(c, Alphabet));
        });
        Assert.True(codes.Distinct().Count() > 95);
    }
}
