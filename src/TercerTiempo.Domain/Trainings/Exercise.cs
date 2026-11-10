using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// Ejercicio de la biblioteca de una plantilla: lo crea el cuerpo técnico una vez y lo mete en los entrenamientos que
// quiera; cada entrenamiento guarda su propia copia (nombre, tipo y duración), así que cambiar o borrar el ejercicio
// de la biblioteca no toca las sesiones ya preparadas.
public sealed class Exercise : AggregateRoot
{
    public Guid SquadId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ExerciseType Type { get; private set; }
    public int DurationMinutes { get; private set; }
    public string? Description { get; private set; }
    public string? Material { get; private set; }
    public Guid? CreatedByCoachId { get; private set; }
    public Guid? CreatedByClubId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Exercise() { }

    public static Exercise Create(Squad squad, SquadEditor editor, string name, ExerciseType type, int durationMinutes, string? description, string? material, DateTimeOffset now)
    {
        var exercise = new Exercise
        {
            Id = NewId(),
            SquadId = squad.Id,
            CreatedByCoachId = editor.CoachId,
            CreatedByClubId = editor.ClubId,
            CreatedAt = now,
        };
        exercise.Update(name, type, durationMinutes, description, material, now);
        return exercise;
    }

    public void Update(string name, ExerciseType type, int durationMinutes, string? description, string? material, DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException(ErrorCode.ExerciseInvalidType);
        if (durationMinutes is < 1 or > TrainingCatalog.MaxDurationMinutes)
            throw new DomainException(ErrorCode.ExerciseDurationRange, TrainingCatalog.MaxDurationMinutes);

        Name = Guard.NotEmpty(name, "nombre", 120);
        Type = type;
        DurationMinutes = durationMinutes;
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.NotEmpty(description, "descripción", 1000);
        Material = string.IsNullOrWhiteSpace(material) ? null : Guard.NotEmpty(material, "material", 300);
        UpdatedAt = now;
    }
}

public interface IExerciseStore
{
    Task<Exercise?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Exercise>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default);
    void Add(Exercise exercise);

    // Quita el enlace de las copias que lo usan en las sesiones (se quedan enteras) y lo borra.
    Task RemoveAsync(Exercise exercise, CancellationToken cancellationToken = default);
}
