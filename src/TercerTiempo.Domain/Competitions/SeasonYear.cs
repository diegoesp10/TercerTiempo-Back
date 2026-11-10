using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Competitions;

// La temporada de toda la app ("2026/2027"): el dato padre. Cada liga tiene su temporada (Season) colgada de ella y de
// esa cuelgan partidos, clasificaciones, plantillas, actas... Elegir otra temporada cambia todos los datos que se ven.
// Solo una es la actual: la que se sincroniza y la única en la que se puede editar.
public sealed class SeasonYear : AggregateRoot
{
    public string Label { get; private set; } = string.Empty;
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public bool IsCurrent { get; private set; }

    private SeasonYear() { }

    public static SeasonYear Create(string label, DateOnly startsOn, DateOnly endsOn)
    {
        if (endsOn <= startsOn)
            throw new DomainException(ErrorCode.CompetitionSeasonDates);

        return new SeasonYear
        {
            Id = NewId(),
            Label = Guard.NotEmpty(label, "temporada", 20),
            StartsOn = startsOn,
            EndsOn = endsOn,
        };
    }

    public bool Covers(DateOnly date) => date >= StartsOn && date <= EndsOn;

    // Marca una como actual y desmarca las demás.
    public static void MarkCurrent(IEnumerable<SeasonYear> all, Guid seasonYearId)
    {
        var list = all.ToList();
        if (list.All(s => s.Id != seasonYearId))
            throw new DomainException(ErrorCode.SeasonNotFound, seasonYearId);

        foreach (var season in list)
            season.IsCurrent = season.Id == seasonYearId;
    }
}

public interface ISeasonYearStore
{
    // De la más reciente a la más antigua.
    Task<IReadOnlyList<SeasonYear>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SeasonYear>> GetAllForUpdateAsync(CancellationToken cancellationToken = default);
    void Add(SeasonYear seasonYear);
}
