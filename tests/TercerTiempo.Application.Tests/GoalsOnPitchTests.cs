using TercerTiempo.Application.Performances;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Application.Tests;

// Los goles que encajó el equipo con cada jugador en el campo, del acta oficial cerrada: cuentan en su nota en vez de
// todos los del partido.
public class GoalsOnPitchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Each_player_only_carries_the_goals_conceded_while_he_was_on_the_pitch()
    {
        // Somos el local. El rival marca en el 34 y de penalti en el 60, y un compañero en propia puerta en el 80.
        // Cirisuelo se fue en el 24; Pelayo entró en el 60, en el mismo minuto que el penalti, que se toma como anterior.
        var sheet = Sheet(3,
        [
            new(MatchSide.Away, OfficialEventType.Goal, 34, null, "RIVAL", null),
            new(MatchSide.Away, OfficialEventType.PenaltyGoal, 60, null, "RIVAL", null),
            new(MatchSide.Home, OfficialEventType.OwnGoal, 80, null, "PELAYO", null),
            new(MatchSide.Home, OfficialEventType.YellowCard, 50, null, "CIRISUELO", null),
            new(MatchSide.Home, OfficialEventType.Goal, 70, null, "PORTERO", null),
        ]);

        Assert.Equal(3, Conceded(sheet, "PORTERO", 3));
        Assert.Equal(0, Conceded(sheet, "CIRISUELO", 3));
        Assert.Equal(1, Conceded(sheet, "PELAYO", 3));
        Assert.Null(Conceded(sheet, "BANQUILLO", 3));
    }

    [Fact]
    public void If_the_goals_of_the_sheet_do_not_add_up_with_the_score_it_is_not_known()
    {
        var missingOne = Sheet(1, [new(MatchSide.Away, OfficialEventType.Goal, 34, null, "RIVAL", null)]);

        Assert.Null(Conceded(missingOne, "PORTERO", 2));
        Assert.Null(Conceded(missingOne, "PORTERO", null));
    }

    private static OfficialMatchSheet Sheet(int awayGoals, IReadOnlyList<OfficialEventData> events)
    {
        var sheet = OfficialMatchSheet.Create(Guid.NewGuid(), "asturfutbol", "1", null, Now);
        sheet.Apply(new OfficialSheetData(null, "Local", "Visitante", 1, awayGoals,
        [
            new(MatchSide.Home, 1, "PORTERO", true, 0, null, 90, 1, 0, 0, 0, false),
            new(MatchSide.Home, 4, "CIRISUELO", true, 0, 24, 24, 0, 0, 0, 1, false),
            new(MatchSide.Home, 9, "PELAYO", false, 60, null, 30, 0, 0, 1, 0, false),
            new(MatchSide.Home, 7, "BANQUILLO", false, null, null, 0, 0, 0, 0, 0, false),
        ], events), Now);
        return sheet;
    }

    private static int? Conceded(OfficialMatchSheet sheet, string name, int? goalsAgainst) =>
        GoalsOnPitch.Conceded(sheet, MatchSide.Home, sheet.Lines.Single(l => l.FullName == name), goalsAgainst);
}
