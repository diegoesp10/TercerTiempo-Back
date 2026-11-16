using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Domain.Tests;

// La web publica a algunos jugadores dos veces, con distinto nombre e identificador: son la misma persona.
public class RosterPeopleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();

    [Theory]
    [InlineData("Lucas Paredes", "Lucas Paredes Rivera")]
    [InlineData("Alvaro Becares Fernandez", "Álvaro Becares Fernández")]
    [InlineData("Martín Rodeiro", "Martin Rodeiro Perez")]
    [InlineData("Álex Cueva", "Alejandro Cueva Vilaboy")]
    [InlineData("Nacho Cabal", "Ignacio Cabal González")]
    [InlineData("Mateo Álvarez", "Mateo José Álvarez Álvarez")]
    [InlineData("David Frade Liñera", "David Frade Liñeira")]
    public void The_same_person_written_with_more_or_fewer_words(string one, string other)
    {
        Assert.True(PersonName.SamePerson(NameNormalizer.Normalize(one), NameNormalizer.Normalize(other)));
        Assert.True(PersonName.SamePerson(NameNormalizer.Normalize(other), NameNormalizer.Normalize(one)));
    }

    [Theory]
    [InlineData("Juan García López", "Juan García Pérez")]
    [InlineData("Mateo García Martínez", "Mateo García Mazo")]
    [InlineData("Pablo Cabal", "Nacho Cabal")]
    [InlineData("Lucas Paredes", "Lucas Rivera")]
    [InlineData("Alfonso Rueda García", "Alfonso Cosio González")]
    [InlineData("Pablo Fernández Flórez", "Pablo Flores Rastrillo")]
    [InlineData("Desmond", "Ogbogbodu")]
    [InlineData("Hugo", "Hugo Betarte Pérez")]
    public void Two_people_who_share_part_of_the_name(string one, string other)
    {
        Assert.False(PersonName.SamePerson(NameNormalizer.Normalize(one), NameNormalizer.Normalize(other)));
        Assert.False(PersonName.SamePerson(NameNormalizer.Normalize(other), NameNormalizer.Normalize(one)));
    }

    [Fact]
    public void The_cards_of_the_same_person_become_one_with_the_most_complete_name()
    {
        var shortCard = Entry("Lucas Paredes", null);
        var fullCard = Entry("Lucas Paredes Rivera", Position.Forward);
        var other = Entry("Marco Huelga Onís", null);

        var people = RosterPeople.Group([shortCard, fullCard, other]);

        Assert.Equal(2, people.Count);
        var lucas = people.Single(p => p.Entries.Count == 2);
        Assert.Equal((fullCard.Id, Position.Forward), (lucas.Main.Id, lucas.Position));
        Assert.Contains(shortCard, lucas.Entries);
    }

    [Fact]
    public void What_the_club_wrote_is_the_card_that_shows()
    {
        var published = Entry("Lucas Paredes Rivera", Position.Forward);
        var fromClub = RosterEntry.CreateByClub(_teamId, _seasonId, "Lucas Paredes", null, Now);

        var lucas = Assert.Single(RosterPeople.Group([published, fromClub]));

        Assert.Equal((fromClub.Id, Position.Forward), (lucas.Main.Id, lucas.Position));
    }

    [Fact]
    public void A_short_name_that_fits_two_different_people_stays_on_its_own()
    {
        var people = RosterPeople.Group([Entry("Mateo García", null), Entry("Mateo García Martínez", null), Entry("Mateo García Mazo", null)]);

        Assert.Equal(3, people.Count);
        Assert.All(people, p => Assert.Single(p.Entries));
    }

    [Fact]
    public void Two_cards_known_to_be_two_players_are_not_merged()
    {
        var people = RosterPeople.Group([Entry("Lucas Paredes", null), Entry("Lucas Paredes Rivera", null)], (_, _) => true);

        Assert.Equal(2, people.Count);
    }

    private RosterEntry Entry(string name, Position? position) => RosterEntry.Create(_teamId, _seasonId, name, position, null, Now);
}
