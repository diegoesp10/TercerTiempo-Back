using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

/// <summary>Liga o competición (p. ej. "Regional Preferente de Asturias") con sus temporadas.</summary>
public sealed class Competition : AggregateRoot
{
    private readonly List<Season> _seasons = [];

    public string Name { get; private set; } = string.Empty;
    public string Region { get; private set; } = string.Empty;
    public ExternalRef? ExternalRef { get; private set; }

    public IReadOnlyCollection<Season> Seasons => _seasons.AsReadOnly();

    private Competition() { }

    public static Competition Create(string name, string region, ExternalRef? externalRef) =>
        new()
        {
            Id = NewId(),
            Name = Guard.NotEmpty(name, "nombre", 150),
            Region = Guard.NotEmpty(region, "región", 100),
            ExternalRef = externalRef,
        };

    public Season AddSeason(string label, DateOnly startsOn, DateOnly endsOn, ExternalRef? externalRef)
    {
        if (endsOn <= startsOn)
            throw new DomainException("La temporada debe terminar después de empezar.");

        if (_seasons.Any(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"La temporada '{label}' ya existe en esta competición.");

        var season = Season.Create(Id, label, startsOn, endsOn, externalRef);
        _seasons.Add(season);
        return season;
    }

    public Season? CurrentSeason => _seasons.FirstOrDefault(s => s.IsCurrent);

    public void MarkCurrent(Guid seasonId)
    {
        if (_seasons.All(s => s.Id != seasonId))
            throw new DomainException("La temporada no pertenece a esta competición.");

        foreach (var season in _seasons)
            season.SetCurrent(season.Id == seasonId);
    }

    public void UpdateDetails(string name, string region)
    {
        Name = Guard.NotEmpty(name, "nombre", 150);
        Region = Guard.NotEmpty(region, "región", 100);
    }
}
