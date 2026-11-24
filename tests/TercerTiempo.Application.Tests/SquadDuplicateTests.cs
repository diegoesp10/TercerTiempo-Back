using TercerTiempo.Application.Squads;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// Qué fichas de una plantilla son la misma persona: las que vienen de fichas de la federación que se juntan en una.
public class SquadDuplicateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();

    [Fact]
    public void A_player_published_twice_keeps_the_card_of_his_main_entry()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var shortCard = Entry("Lucas Paredes");
        var fullCard = Entry("Lucas Paredes Rivera");
        var shortPlayer = squad.AddPlayer(shortCard.FullName, Position.Forward, null, shortCard.Id, Now);
        var fullPlayer = squad.AddPlayer(fullCard.FullName, Position.Forward, null, fullCard.Id, Now);
        var person = Assert.Single(RosterPeople.Group([shortCard, fullCard]));

        var duplicate = Assert.Single(SquadDuplicates.Find(squad, [person]));

        Assert.Equal(person.Main.Id, duplicate.Keep.RosterEntryId);
        var other = Assert.Single(duplicate.Duplicates);
        Assert.Contains(other, new[] { shortPlayer, fullPlayer });
        Assert.NotEqual(duplicate.Keep, other);
    }

    [Fact]
    public void Two_cards_in_the_same_official_sheet_are_two_players_and_are_not_merged()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var shortCard = Entry("Lucas Paredes");
        var fullCard = Entry("Lucas Paredes Rivera");
        squad.AddPlayer(shortCard.FullName, Position.Forward, null, shortCard.Id, Now);
        squad.AddPlayer(fullCard.FullName, Position.Forward, null, fullCard.Id, Now);

        var people = RosterPeople.Group([shortCard, fullCard], (_, _) => true);

        Assert.Equal(2, people.Count);
        Assert.Empty(SquadDuplicates.Find(squad, people));
    }

    [Fact]
    public void Players_without_a_federation_card_or_already_gone_are_left_alone()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var shortCard = Entry("Lucas Paredes");
        var fullCard = Entry("Lucas Paredes Rivera");
        squad.AddPlayer(fullCard.FullName, Position.Forward, null, fullCard.Id, Now);
        var gone = squad.AddPlayer(shortCard.FullName, Position.Forward, null, shortCard.Id, Now);
        squad.RemovePlayer(gone.Id, Now);
        squad.AddPlayer("Lucas", Position.Forward, null, null, Now);

        Assert.Empty(SquadDuplicates.Find(squad, RosterPeople.Group([shortCard, fullCard])));
    }

    [Fact]
    public void Only_squads_with_two_names_of_the_same_person_are_checked()
    {
        Assert.True(SquadDuplicates.MayHaveDuplicates(["lucas paredes", "pelayo fernandez", "lucas paredes rivera"]));
        Assert.False(SquadDuplicates.MayHaveDuplicates(["lucas paredes", "pelayo fernandez", "lucas garcia"]));
    }

    private RosterEntry Entry(string name) => RosterEntry.Create(_teamId, _seasonId, name, Position.Forward, null, Now);
}
