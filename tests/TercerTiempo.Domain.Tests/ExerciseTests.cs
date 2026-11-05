using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Domain.Tests;

public class ExerciseTests
{
    [Fact]
    public void Create_keeps_who_wrote_it_and_trims_the_optional_texts()
    {
        var (squad, coachId) = TestData.NewSquad();

        var exercise = Exercise.Create(squad, SquadEditor.Coach(coachId), "Rondo 4x2", ExerciseType.Technical, 15, "  ", "Conos y petos", TestData.Now);

        Assert.Equal((squad.Id, coachId, (Guid?)null), (exercise.SquadId, exercise.CreatedByCoachId, exercise.CreatedByClubId));
        Assert.Equal(("Rondo 4x2", ExerciseType.Technical, 15), (exercise.Name, exercise.Type, exercise.DurationMinutes));
        Assert.Null(exercise.Description);
        Assert.Equal("Conos y petos", exercise.Material);
    }

    [Fact]
    public void Type_and_duration_are_checked_on_create_and_update()
    {
        var (squad, coachId) = TestData.NewSquad();
        var editor = SquadEditor.Coach(coachId);
        var exercise = Exercise.Create(squad, editor, "Rondo", ExerciseType.Technical, 15, null, null, TestData.Now);

        var badType = Assert.Throws<DomainException>(() => Exercise.Create(squad, editor, "Raro", (ExerciseType)55, 15, null, null, TestData.Now));
        var zero = Assert.Throws<DomainException>(() => exercise.Update("Rondo", ExerciseType.Technical, 0, null, null, TestData.Now));
        var tooLong = Assert.Throws<DomainException>(() => exercise.Update("Rondo", ExerciseType.Technical, TrainingCatalog.MaxDurationMinutes + 1, null, null, TestData.Now));

        Assert.Equal(ErrorCode.ExerciseInvalidType, badType.Code);
        Assert.Equal(ErrorCode.ExerciseDurationRange, zero.Code);
        Assert.Equal(ErrorCode.ExerciseDurationRange, tooLong.Code);
        Assert.Throws<DomainException>(() => exercise.Update(" ", ExerciseType.Technical, 15, null, null, TestData.Now));
    }

    [Fact]
    public void The_catalog_falls_back_to_other_for_unknown_values()
    {
        Assert.Equal("Entrenamiento de la semana", TrainingCatalog.Info(TrainingKind.Season).Label);
        Assert.Equal("Otro", TrainingCatalog.Info((TrainingKind)123).Label);
        Assert.Equal("Balón parado", TrainingCatalog.Info(ExerciseType.SetPieces).Label);
        Assert.Equal("Otro", TrainingCatalog.Info((ExerciseType)123).Label);
    }
}
