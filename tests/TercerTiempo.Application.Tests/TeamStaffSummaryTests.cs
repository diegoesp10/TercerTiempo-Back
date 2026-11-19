using TercerTiempo.Application.Teams.GetTeam;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Tests;

public sealed class TeamStaffSummaryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid[] SheetIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();

    private static SheetStaffEntry Row(int matchday, OfficialStaffRole role, string label, string name) =>
        new(SheetIds[matchday - 1], Start.AddDays(7 * (matchday - 1)), role, label, name);

    private static IEnumerable<SheetStaffEntry> Coach(string name, params int[] matchdays) =>
        matchdays.Select(d => Row(d, OfficialStaffRole.HeadCoach, "Entrenador", name));

    [Fact]
    public void The_same_person_written_two_ways_is_one_member_with_all_his_actas()
    {
        var rows = Coach("FERNANDEZ PARDO, ABEL PABLO", 1, 2, 3).Concat(Coach("FERNANDEZ, ABEL PABLO", 4)).ToList();

        var staff = TeamStaffSummary.Build(rows, [], includeAppOnly: false, includeProfiles: false);

        var member = Assert.Single(staff.Members);
        Assert.Equal("Abel Pablo Fernandez Pardo", member.Name);
        Assert.False(member.HasProfile);
        Assert.Equal(4, member.Appearances);
        Assert.Equal(4, staff.Sheets);
        Assert.True(member.Current);
    }

    [Fact]
    public void The_head_coach_goes_first_and_whoever_left_goes_after_the_current_ones()
    {
        var rows = Coach("GARCIA DE LA FUENTE, JOSE", 1, 2, 3, 4, 5)
            .Concat(Coach("MENENDEZ CUETO, XURDE", 6, 7, 8, 9))
            .Concat(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }.Select(d => Row(d, OfficialStaffRole.TeamDelegate, "DEL. Equipo", "SUAREZ, ANA")))
            .Concat([Row(9, OfficialStaffRole.AssistantCoach, "2ºEntrenador", "LOPEZ, IKER")])
            .ToList();

        var staff = TeamStaffSummary.Build(rows, [], includeAppOnly: false, includeProfiles: false);

        Assert.Equal(["Xurde Menendez Cueto", "Iker Lopez", "Ana Suarez", "Jose Garcia de la Fuente"], staff.Members.Select(m => m.Name));
        Assert.Equal([true, true, true, false], staff.Members.Select(m => m.Current));
        Assert.Equal(new DateOnly(2026, 10, 11), staff.Members[^1].LastSeen);
    }

    [Fact]
    public void Who_the_club_named_in_the_app_takes_the_name_of_his_profile_and_links_to_it_only_for_signed_in_accounts()
    {
        var coachId = Guid.NewGuid();
        var rows = Coach("FERNANDEZ PARDO, ABEL PABLO", 1, 2).ToList();
        AppStaffEntry[] appointed = [new(coachId, "Abel Pablo Fernández Pardo", ["abel pablo fernandez pardo"], StaffRole.HeadCoach, Active: true)];

        var signedIn = Assert.Single(TeamStaffSummary.Build(rows, appointed, includeAppOnly: true, includeProfiles: true).Members);
        var anonymous = Assert.Single(TeamStaffSummary.Build(rows, appointed, includeAppOnly: false, includeProfiles: false).Members);

        Assert.Equal("Abel Pablo Fernández Pardo", signedIn.Name);
        Assert.Equal(coachId, signedIn.CoachId);
        Assert.Equal(StaffRole.HeadCoach, signedIn.Role);
        Assert.Equal("Abel Pablo Fernández Pardo", anonymous.Name);
        Assert.Null(anonymous.CoachId);
        Assert.Null(anonymous.Role);
        Assert.True(signedIn.HasProfile);
        Assert.True(anonymous.HasProfile);
    }

    [Fact]
    public void Staff_named_in_the_app_but_not_in_any_acta_only_shows_to_signed_in_accounts()
    {
        var rows = Coach("FERNANDEZ PARDO, ABEL PABLO", 1).ToList();
        AppStaffEntry[] appointed = [new(Guid.NewGuid(), "Laura Roces", ["laura roces"], StaffRole.Physio, Active: true)];

        var signedIn = TeamStaffSummary.Build(rows, appointed, includeAppOnly: true, includeProfiles: true);
        var anonymous = TeamStaffSummary.Build(rows, appointed, includeAppOnly: false, includeProfiles: false);

        var physio = Assert.Single(signedIn.Members, m => m.Name == "Laura Roces");
        Assert.Null(physio.OfficialRole);
        Assert.Equal(0, physio.Appearances);
        Assert.DoesNotContain(anonymous.Members, m => m.Name == "Laura Roces");
    }

    [Fact]
    public void No_presenta_in_the_place_of_the_coach_is_nobody()
    {
        var rows = Coach("NO PRESENTA", 1, 2).ToList();

        var staff = TeamStaffSummary.Build(rows, [], includeAppOnly: false, includeProfiles: false);

        Assert.Empty(staff.Members);
        Assert.Equal(0, staff.Sheets);
    }
}
