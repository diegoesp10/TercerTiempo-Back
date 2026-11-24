using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Domain.Tests;

// Dónde se lesionó: el partido o el entrenamiento queda en la lesión, con su día. En ese mismo partido o entrenamiento el
// jugador estuvo (hasta lesionarse jugó o entrenó); desde ahí, de baja.
public class InjuryOccasionTests
{
    private static readonly InjuryTypeDefinition Sprain = TestData.InjuryType(InjuryTypeCodes.AnkleSprain);

    private static (Squad Squad, SquadEditor Editor, SquadPlayer Pelayo) SquadWithPelayo()
    {
        var (squad, coachId) = TestData.NewSquad();
        return (squad, SquadEditor.Coach(coachId), squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now));
    }

    private static InjuryOccasion Match(DateOnly day) => new(InjuryOccasionKind.Match, Guid.NewGuid(), day, "J11 · Europa de Nava – CD Vallobín");

    private static InjuryOccasion Training(DateOnly day) => new(InjuryOccasionKind.Training, Guid.NewGuid(), day, "Táctico");

    [Fact]
    public void It_keeps_where_it_happened_and_the_report_cannot_be_earlier()
    {
        var (squad, editor, pelayo) = SquadWithPelayo();
        var match = Match(TestData.Today.AddDays(-2));

        var injury = PlayerInjury.Create(squad, pelayo, editor, Sprain, null, TestData.Today.AddDays(-1), null, TestData.Now, occasion: match);

        Assert.Equal(match, injury.Occasion);
        Assert.Equal(match.Id, injury.MatchId);
        Assert.Null(injury.TrainingSessionId);
        var early = Assert.Throws<DomainException>(() => injury.Update(Sprain, null, TestData.Today.AddDays(-3), null, TestData.Now, match));
        Assert.Equal(ErrorCode.InjuryBeforeOccasion, early.Code);

        var training = Training(TestData.Today.AddDays(-1));
        injury.Update(Sprain, null, TestData.Today.AddDays(-1), null, TestData.Now, training);
        Assert.Null(injury.MatchId);
        Assert.Equal((training.Id, training.Day, "Táctico"), (injury.TrainingSessionId, injury.OccasionDay, injury.OccasionLabel));

        // En otro sitio o no se sabe.
        injury.Update(Sprain, null, TestData.Today.AddDays(-1), null, TestData.Now);
        Assert.Null(injury.Occasion);
        Assert.Null(injury.OccasionDay);
    }

    [Fact]
    public void Where_he_got_hurt_he_was_there_and_from_then_on_he_is_out()
    {
        var (squad, editor, pelayo) = SquadWithPelayo();
        var day = TestData.Today.AddDays(-3);
        var training = Training(day);
        var injury = PlayerInjury.Create(squad, pelayo, editor, Sprain, null, day, null, TestData.Now, occasion: training);
        var calendar = new InjuryCalendar([injury]);

        Assert.Null(calendar.On(pelayo.Id, day, training.Id));
        Assert.Same(injury, calendar.On(pelayo.Id, day));
        Assert.Same(injury, calendar.On(pelayo.Id, day.AddDays(1), Guid.NewGuid()));
        Assert.Same(injury, calendar.At(pelayo.Id, training.Id));
        Assert.Null(calendar.At(pelayo.Id, Guid.NewGuid()));
    }

    [Fact]
    public void The_session_where_he_got_hurt_counts_as_attended_and_the_next_ones_as_injured()
    {
        var (squad, editor, pelayo) = SquadWithPelayo();
        var first = TestData.Now.AddDays(1);
        var sessions = Enumerable.Range(0, 3)
            .Select(d => TrainingSession.Plan(squad, editor, TrainingKind.Season, null, first.AddDays(d * 2), 90, "Campo de La Florida", null, TestData.Now))
            .ToList();
        var day = DateOnly.FromDateTime(first.UtcDateTime);
        var here = new InjuryOccasion(InjuryOccasionKind.Training, sessions[0].Id, day, sessions[0].Title);
        var injury = PlayerInjury.Create(squad, pelayo, editor, Sprain, null, day, null, TestData.Now, occasion: here);
        var calendar = new InjuryCalendar([injury]);
        var slots = sessions.Select(s => new TrainingSlot(s.Id, squad.Id, s.StartsAt, s.DurationMinutes)).ToList();

        var tally = TrainingTally.For(pelayo, squad.Id, slots, new Dictionary<(Guid, Guid), AttendanceStatus>(), first.AddDays(10),
            slot => calendar.On(pelayo.Id, DateOnly.FromDateTime(slot.StartsAt.UtcDateTime), slot.SessionId) is not null);

        Assert.Equal(new PlayerTrainingTally(Sessions: 1, Attended: 1, Missed: 0, Warned: 0, AttendedMinutes: 90, Injured: 2), tally);
    }

    [Fact]
    public void A_session_changed_while_being_held_is_copied_and_a_deleted_one_is_let_go()
    {
        var (squad, editor, pelayo) = SquadWithPelayo();
        var day = TestData.Today.AddDays(-1);
        var training = Training(day);
        var injury = PlayerInjury.Create(squad, pelayo, editor, Sprain, null, day, null, TestData.Now, occasion: training);

        injury.RefreshOccasion(training with { Label = "Físico" }, TestData.Now);
        Assert.Equal("Físico", injury.OccasionLabel);
        injury.RefreshOccasion(Training(day) with { Label = "Otra sesión" }, TestData.Now);
        Assert.Equal("Físico", injury.OccasionLabel);
        // Movida a después del parte, ya no puede ser donde se lesionó.
        injury.RefreshOccasion(training with { Day = day.AddDays(1) }, TestData.Now);
        Assert.Null(injury.Occasion);

        var other = PlayerInjury.Create(squad, pelayo, editor, Sprain, null, day, null, TestData.Now, occasion: Training(day));
        other.ForgetOccasion(TestData.Now);
        Assert.Null(other.Occasion);
        Assert.Null(other.TrainingSessionId);
    }

    [Fact]
    public void The_continuation_in_the_next_season_keeps_where_it_happened()
    {
        var (lastSeason, coachId) = TestData.NewSquad();
        var then = lastSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var (newSeason, _) = TestData.NewSquad();
        var now = newSeason.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now);
        var match = Match(TestData.Today.AddDays(-30));
        var old = PlayerInjury.Create(lastSeason, then, SquadEditor.Coach(coachId), Sprain, null, TestData.Today.AddDays(-30), null, TestData.Now.AddDays(-30),
            occasion: match);

        var next = old.ContinueIn(newSeason, now, TestData.Now);

        Assert.Equal(match, next.Occasion);
    }
}
