using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

// La lesión que sigue abierta al cambiar de temporada pasa a la plantilla nueva con su parte (el contador no vuelve a
// cero) y la de la temporada anterior queda como historial, sin poder tocarse más que para borrarla.
public class InjuryContinuationTests
{
    [Fact]
    public void The_continuation_keeps_the_report_and_the_old_one_counts_until_it_passed()
    {
        var (lastSeason, coachId) = TestData.NewSquad();
        var then = lastSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var (newSeason, _) = TestData.NewSquad();
        var now = newSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var reportedAt = TestData.Now.AddDays(-40);
        var old = PlayerInjury.Create(lastSeason, then, SquadEditor.Coach(coachId), TestData.InjuryType(InjuryTypeCodes.MuscleTear, 60), "Isquio",
            TestData.Today.AddDays(-40), TestData.Today.AddDays(20), reportedAt);
        var passedAt = TestData.Now.AddDays(-10);

        var next = old.ContinueIn(newSeason, now, passedAt);

        Assert.Equal((newSeason.Id, now.Id, old.Id), (next.SquadId, next.SquadPlayerId, next.ContinuesInjuryId));
        Assert.Equal((old.Type, old.Reason, old.StartedOn, old.ExpectedReturnOn, old.InitialExpectedReturnOn, old.ReportedAt),
            (next.Type, next.Reason, next.StartedOn, next.ExpectedReturnOn, next.InitialExpectedReturnOn, next.ReportedAt));
        Assert.Equal((true, 40, 20), (next.IsActive, next.DaysOut(TestData.Today), next.ExpectedDays - next.DaysOut(TestData.Today)));

        Assert.Equal((false, SpainTime.Today(passedAt), 30), (old.IsActive, old.ContinuedOn, old.DaysOut(TestData.Today)));
        Assert.True(old.CoversDay(TestData.Today.AddDays(-20)));
    }

    [Fact]
    public void The_old_one_is_only_consulted()
    {
        var (lastSeason, coachId) = TestData.NewSquad();
        var then = lastSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var (newSeason, _) = TestData.NewSquad();
        var now = newSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var sprain = TestData.InjuryType(InjuryTypeCodes.AnkleSprain);
        var old = PlayerInjury.Create(lastSeason, then, SquadEditor.Coach(coachId), sprain, null, TestData.Today.AddDays(-12), null, TestData.Now.AddDays(-12));
        old.ContinueIn(newSeason, now, TestData.Now);

        Assert.Equal(ErrorCode.InjuryContinued, Assert.Throws<DomainException>(() => old.Close(TestData.Today, TestData.Now)).Code);
        Assert.Equal(ErrorCode.InjuryContinued, Assert.Throws<DomainException>(() => old.Reopen(TestData.Now)).Code);
        Assert.Equal(ErrorCode.InjuryContinued, Assert.Throws<DomainException>(() => old.Update(sprain, "Otro", old.StartedOn, null, TestData.Now)).Code);
        Assert.Equal(ErrorCode.InjuryContinued, Assert.Throws<DomainException>(() =>
            PlayerInjury.Create(lastSeason, then, SquadEditor.Coach(coachId), sprain, null, TestData.Today, null, TestData.Now, relapseOf: old)).Code);
        // Y no pasa dos veces.
        Assert.Equal(ErrorCode.InjuryAlreadyClosed, Assert.Throws<DomainException>(() => old.ContinueIn(newSeason, now, TestData.Now)).Code);
    }

    [Fact]
    public void It_only_passes_to_an_active_player_of_another_squad()
    {
        var (lastSeason, coachId) = TestData.NewSquad();
        var then = lastSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var (newSeason, _) = TestData.NewSquad();
        var left = newSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        newSeason.RemovePlayer(left.Id, TestData.Now);
        var injury = PlayerInjury.Create(lastSeason, then, SquadEditor.Coach(coachId), TestData.InjuryType(InjuryTypeCodes.AnkleSprain), null,
            TestData.Today.AddDays(-3), null, TestData.Now);

        Assert.Equal(ErrorCode.SquadPlayerNotMember, Assert.Throws<DomainException>(() => injury.ContinueIn(newSeason, left, TestData.Now)).Code);
        Assert.Equal(ErrorCode.SquadPlayerNotMember, Assert.Throws<DomainException>(() => injury.ContinueIn(lastSeason, then, TestData.Now)).Code);
        Assert.True(injury.IsActive);
    }
}
