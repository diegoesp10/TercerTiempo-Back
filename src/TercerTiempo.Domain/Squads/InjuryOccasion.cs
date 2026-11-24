namespace TercerTiempo.Domain.Squads;

public enum InjuryOccasionKind
{
    Match = 1,
    Training = 2,
}

// Dónde se lesionó: un partido de liga del equipo o un entrenamiento de la plantilla. Lleva copiados su día y su nombre
// para saber cuándo fue sin ir a buscarlo.
public sealed record InjuryOccasion(InjuryOccasionKind Kind, Guid Id, DateOnly Day, string Label)
{
    public const int LabelMaxLength = 200;
}
