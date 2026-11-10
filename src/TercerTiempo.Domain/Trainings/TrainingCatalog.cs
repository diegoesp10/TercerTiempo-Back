namespace TercerTiempo.Domain.Trainings;

public enum TrainingKind
{
    // Entrenamiento de la semana, en temporada.
    Season = 1,
    Preseason = 2,
    Recovery = 3,
    MatchPreparation = 4,
    Other = 99,
}

public enum ExerciseType
{
    Warmup = 1,
    Technical = 2,
    Tactical = 3,
    Physical = 4,
    Goalkeeping = 5,
    SetPieces = 6,
    Game = 7,
    Cooldown = 8,
    Other = 99,
}

// Lo que se sabe de cada jugador en un entrenamiento: cuenta con venir, ha avisado de que falta, vino o faltó sin avisar.
public enum AttendanceStatus
{
    Expected = 1,
    WillMiss = 2,
    Attended = 3,
    Missed = 4,
}

public enum CallUpStatus
{
    Draft = 1,
    Published = 2,
}

// Lo que hace cada jugador con la convocatoria: el convocado la firma; el que no va, se da por enterado. No hay avisos
// de «no puedo» para los partidos: la convocatoria es la que emite el cuerpo técnico y lo que cambie se habla con él.
public enum CallUpResponse
{
    Pending = 1,
    Confirmed = 2,
    Acknowledged = 4,
}

public sealed record TrainingKindInfo(TrainingKind Kind, string Label, string Description);

public sealed record ExerciseTypeInfo(ExerciseType Type, string Label);

public static class TrainingCatalog
{
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 300;
    public const int MaxItems = 40;
    public const int MaxSeriesSessions = 60;

    public static readonly IReadOnlyList<TrainingKindInfo> Kinds =
    [
        new(TrainingKind.Season, "Entrenamiento de la semana", "El entrenamiento normal de la temporada."),
        new(TrainingKind.Preseason, "Pretemporada", "Carga física y puesta a punto antes de empezar la liga."),
        new(TrainingKind.Recovery, "Recuperación", "Sesión suave tras el partido."),
        new(TrainingKind.MatchPreparation, "Preparación del partido", "La sesión previa al partido: estrategia, balón parado, once."),
        new(TrainingKind.Other, "Otro", "Cualquier otra sesión."),
    ];

    public static readonly IReadOnlyList<ExerciseTypeInfo> ExerciseTypes =
    [
        new(ExerciseType.Warmup, "Calentamiento"),
        new(ExerciseType.Technical, "Técnico"),
        new(ExerciseType.Tactical, "Táctico"),
        new(ExerciseType.Physical, "Físico"),
        new(ExerciseType.Goalkeeping, "Porteros"),
        new(ExerciseType.SetPieces, "Balón parado"),
        new(ExerciseType.Game, "Partidillo"),
        new(ExerciseType.Cooldown, "Vuelta a la calma"),
        new(ExerciseType.Other, "Otro"),
    ];

    public static TrainingKindInfo Info(TrainingKind kind) => Kinds.FirstOrDefault(k => k.Kind == kind) ?? Kinds[^1];

    public static ExerciseTypeInfo Info(ExerciseType type) => ExerciseTypes.FirstOrDefault(t => t.Type == type) ?? ExerciseTypes[^1];
}
