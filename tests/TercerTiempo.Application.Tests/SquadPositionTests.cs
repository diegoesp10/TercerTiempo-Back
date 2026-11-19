using TercerTiempo.Application.Clubs.Roster;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.ManagePlayers;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Tests;

// Ningún jugador sin posición: la federación no la publica de todos, así que el club la elige al dar de alta, editar o
// hacer ficha, y la que la federación publique después se le pone sola a quien no tenía ninguna.
public class SquadPositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();

    [Fact]
    public void The_position_the_federation_publishes_later_completes_the_player_without_one()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var card = Entry("Marco Huelga Onís", null);
        SquadOpening.ImportRoster(squad, [card], Now);
        Assert.Null(squad.ActivePlayers.Single().Position);

        card.Update("Marco Huelga Onís", Position.Midfielder, Now);
        var completed = SquadOpening.CompletePositions(squad, RosterPeople.Group([card]));

        Assert.Equal(Position.Midfielder, Assert.Single(completed).Position);
    }

    [Fact]
    public void A_player_published_twice_takes_the_position_of_his_other_card()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var shortCard = Entry("Lucas Paredes", null);
        var player = squad.AddPlayer(shortCard.FullName, null, null, shortCard.Id, Now);
        var fullCard = Entry("Lucas Paredes Rivera", Position.Forward);

        SquadOpening.ImportRoster(squad, [shortCard, fullCard], Now);

        Assert.Equal(Position.Forward, player.Position);
        Assert.Single(squad.ActivePlayers);
    }

    [Fact]
    public void The_position_the_club_chose_is_never_overwritten()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var card = Entry("Alfonso Cosio González", Position.Forward);
        var player = squad.AddPlayer(card.FullName, Position.Defender, null, card.Id, Now);

        Assert.Empty(SquadOpening.CompletePositions(squad, RosterPeople.Group([card])));
        Assert.Equal(Position.Defender, player.Position);
    }

    [Fact]
    public void A_player_added_by_hand_only_takes_it_when_his_name_fits_one_person()
    {
        var squad = Squad.Open(_teamId, _seasonId, Now);
        var pelayo = squad.AddPlayer("Pelayo", null, null, null, Now);
        var nacho = squad.AddPlayer("Ignacio Alfonso García Martínez", null, null, null, Now);
        var roster = new[]
        {
            Entry("Pelayo González Álvarez", Position.Defender),
            Entry("Pelayo Secades Díaz", Position.Forward),
            Entry("Ignacio Alfonso García Martínez", Position.Goalkeeper),
        };

        SquadOpening.CompletePositions(squad, RosterPeople.Group(roster));

        // Dos Pelayos en la plantilla publicada: no se sabe cuál es.
        Assert.Null(pelayo.Position);
        Assert.Equal(Position.Goalkeeper, nacho.Position);
    }

    [Fact]
    public void Whoever_adds_edits_or_signs_a_player_has_to_choose_his_position()
    {
        var squadId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        AssertPositionRequired(new AddSquadPlayerValidator().Validate(new AddSquadPlayerCommand(squadId, "Marco Huelga Onís", null, null)));
        AssertPositionRequired(new UpdateSquadPlayerValidator().Validate(new UpdateSquadPlayerCommand(squadId, playerId, "Marco Huelga Onís", null, null)));
        AssertPositionRequired(new PromoteTrialPlayerValidator().Validate(new PromoteTrialPlayerCommand(squadId, playerId, null, null)));
        AssertPositionRequired(new AddClubRosterEntryValidator().Validate(new AddClubRosterEntryCommand(_teamId, "Marco Huelga Onís", null)));
        AssertPositionRequired(new UpdateClubRosterEntryValidator().Validate(new UpdateClubRosterEntryCommand(_teamId, playerId, "Marco Huelga Onís", null)));

        Assert.True(new AddSquadPlayerValidator().Validate(new AddSquadPlayerCommand(squadId, "Marco Huelga Onís", Position.Midfielder, null)).IsValid);
        Assert.False(new AddSquadPlayerValidator().Validate(new AddSquadPlayerCommand(squadId, "Marco Huelga Onís", (Position)9, null)).IsValid);
    }

    private static void AssertPositionRequired(FluentValidation.Results.ValidationResult result) =>
        Assert.Contains(result.Errors, e => e.ErrorCode == ErrorCode.PlayerPositionRequired);

    private RosterEntry Entry(string name, Position? position) => RosterEntry.Create(_teamId, _seasonId, name, position, null, Now);
}
