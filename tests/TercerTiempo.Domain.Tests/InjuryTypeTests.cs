using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

public class InjuryTypeTests
{
    [Fact]
    public void A_retired_type_is_not_offered_for_new_injuries_but_the_ones_that_have_it_keep_it()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var editor = SquadEditor.Coach(coachId);
        var sprain = TestData.InjuryType(InjuryTypeCodes.AnkleSprain);
        var tear = TestData.InjuryType(InjuryTypeCodes.MuscleTear);
        var injury = PlayerInjury.Create(squad, pelayo, editor, sprain, null, TestData.Today, null, TestData.Now);

        sprain.Update(sprain.Label, sprain.AverageDays, sprain.SortOrder, active: false, TestData.Now);
        tear.Update(tear.Label, tear.AverageDays, tear.SortOrder, active: false, TestData.Now);

        // La que ya lo tenía se puede seguir corrigiendo sin cambiar de tipo...
        injury.Update(sprain, "Pisó mal", TestData.Today, TestData.Today.AddDays(10), TestData.Now);
        Assert.Equal((InjuryTypeCodes.AnkleSprain, "Pisó mal"), (injury.Type, injury.Reason));
        // ...pero ni una lesión nueva ni un cambio de tipo pueden elegir uno retirado.
        Assert.Equal(ErrorCode.InjuryTypeRetired, Assert.Throws<DomainException>(() =>
            PlayerInjury.Create(squad, pelayo, editor, sprain, null, TestData.Today, null, TestData.Now)).Code);
        Assert.Equal(ErrorCode.InjuryTypeRetired, Assert.Throws<DomainException>(() =>
            injury.Update(tear, null, TestData.Today, null, TestData.Now)).Code);
    }

    [Fact]
    public void Renaming_a_type_renames_it_in_the_injuries_that_have_it()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var type = TestData.InjuryType(InjuryTypeCodes.Contusion);
        var injury = PlayerInjury.Create(squad, pelayo, SquadEditor.Coach(coachId), type, null, TestData.Today, null, TestData.Now);

        type.Update("Golpe", 4, 3, active: true, TestData.Now);

        Assert.Equal(("Golpe", 4), (injury.TypeDefinition.Label, injury.TypeDefinition.AverageDays));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(731)]
    public void The_average_duration_goes_from_one_day_to_two_years(int days)
    {
        var type = TestData.InjuryType(InjuryTypeCodes.Fracture);

        Assert.Equal(ErrorCode.InjuryTypeAverageDaysInvalid,
            Assert.Throws<DomainException>(() => type.Update(type.Label, days, 0, active: true, TestData.Now)).Code);
    }
}
