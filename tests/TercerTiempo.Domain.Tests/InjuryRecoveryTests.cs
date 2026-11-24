using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

// Lo que tarda de verdad cada lesión: del parte al alta, con lo que se previó al dar el parte y sus recaídas aparte.
public class InjuryRecoveryTests
{
    [Fact]
    public void A_relapse_is_a_new_injury_and_the_recovered_one_keeps_its_return_and_its_days()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var editor = SquadEditor.Coach(coachId);
        var tear = TestData.InjuryType(InjuryTypeCodes.MuscleTear, 21);
        var first = PlayerInjury.Create(squad, pelayo, editor, tear, null, TestData.Today.AddDays(-40), TestData.Today.AddDays(-19), TestData.Now);
        first.Close(TestData.Today.AddDays(-12), TestData.Now);

        // El tipo se retira del catálogo: la recaída es la misma lesión y lo conserva.
        tear.Update(tear.Label, tear.AverageDays, tear.SortOrder, active: false, TestData.Now);
        var relapse = PlayerInjury.Create(squad, pelayo, editor, tear, "Otra vez el isquio", TestData.Today.AddDays(-5), null, TestData.Now, relapseOf: first);

        Assert.Equal((first.Id, InjuryTypeCodes.MuscleTear, true), (relapse.RelapseOfInjuryId, relapse.Type, relapse.IsActive));
        Assert.Equal((TestData.Today.AddDays(-12), 28), (first.ReturnedOn, first.DaysOut(TestData.Today)));
        Assert.Equal(5, relapse.DaysOut(TestData.Today));
    }

    [Fact]
    public void A_relapse_needs_the_return_and_cannot_start_before_it()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var editor = SquadEditor.Coach(coachId);
        var sprain = TestData.InjuryType(InjuryTypeCodes.AnkleSprain);
        var open = PlayerInjury.Create(squad, pelayo, editor, sprain, null, TestData.Today.AddDays(-20), null, TestData.Now);

        Assert.Equal(ErrorCode.InjuryRelapseNeedsReturn, Assert.Throws<DomainException>(() =>
            PlayerInjury.Create(squad, pelayo, editor, sprain, null, TestData.Today, null, TestData.Now, relapseOf: open)).Code);

        open.Close(TestData.Today.AddDays(-6), TestData.Now);
        Assert.Equal(ErrorCode.InjuryRelapseBeforeReturn, Assert.Throws<DomainException>(() =>
            PlayerInjury.Create(squad, pelayo, editor, sprain, null, TestData.Today.AddDays(-7), null, TestData.Now, relapseOf: open)).Code);
    }

    [Fact]
    public void The_forecast_given_with_the_report_stays_when_the_doctor_changes_it_later()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var fracture = TestData.InjuryType(InjuryTypeCodes.Fracture, 60);
        var injury = PlayerInjury.Create(squad, pelayo, SquadEditor.Coach(coachId), fracture, null, TestData.Today, TestData.Today.AddDays(20), TestData.Now);

        // El mismo día se corrige el propio parte...
        injury.Update(fracture, null, TestData.Today, TestData.Today.AddDays(25), TestData.Now.AddHours(2));
        Assert.Equal((TestData.Now, TestData.Today.AddDays(25)), (injury.ReportedAt, injury.InitialExpectedReturnOn));

        // ...y otro día lo que cambia es la previsión: la del parte se queda para comparar.
        injury.Update(fracture, null, TestData.Today, TestData.Today.AddDays(40), TestData.Now.AddDays(3));
        Assert.Equal((TestData.Today.AddDays(25), TestData.Today.AddDays(40)), (injury.InitialExpectedReturnOn, injury.ExpectedReturnOn));
        Assert.Equal((25, 40), (injury.InitialExpectedDays, injury.ExpectedDays));
    }

    [Fact]
    public void Undoing_a_return_given_by_mistake_forgets_when_it_was_recorded()
    {
        var (squad, coachId) = TestData.NewSquad();
        var pelayo = squad.AddPlayer("Pelayo Fernández", Position.Forward, 9, null, TestData.Now);
        var injury = PlayerInjury.Create(squad, pelayo, SquadEditor.Coach(coachId), TestData.InjuryType(InjuryTypeCodes.Contusion), null, TestData.Today.AddDays(-3), null, TestData.Now);

        injury.Close(TestData.Today, TestData.Now.AddHours(1));
        Assert.Equal((TestData.Today, TestData.Now.AddHours(1)), (injury.ReturnedOn, injury.ReturnRecordedAt));

        injury.Reopen(TestData.Now.AddHours(2));
        Assert.True(injury.IsActive);
        Assert.Null(injury.ReturnedOn);
        Assert.Null(injury.ReturnRecordedAt);
    }
}
