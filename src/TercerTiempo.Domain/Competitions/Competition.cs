using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Competitions;

/// <summary>
/// Una liga concreta: categoría + grupo (p. ej. "2ª AsturFútbol · Grupo 1" o "3ª RFEF"), con sus temporadas.
/// Cada competición tiene su propia temporada activa; un equipo participa en una competición por temporada.
/// </summary>
public sealed class Competition : AggregateRoot
{
    private readonly List<Season> _seasons = [];

    public string Name { get; private set; } = string.Empty;

    /// <summary>Categoría sin el grupo: "3ª RFEF", "1ª AsturFútbol", "2ª AsturFútbol"...</summary>
    public string Category { get; private set; } = string.Empty;

    public CompetitionLevel Level { get; private set; }

    /// <summary>Escalón dentro del nivel (1 = el más alto): 1ª RFEF = 1, 3ª RFEF = 3, 2ª AsturFútbol = 2...</summary>
    public int Tier { get; private set; }

    /// <summary>Grupo dentro de la categoría ("1", "2", "A"...), o null si solo hay uno.</summary>
    public string? Group { get; private set; }

    public string Region { get; private set; } = string.Empty;

    /// <summary>Orden de presentación: nivel, escalón y grupo.</summary>
    public int SortOrder { get; private set; }

    public ExternalRef? ExternalRef { get; private set; }

    public IReadOnlyCollection<Season> Seasons => _seasons.AsReadOnly();

    private Competition() { }

    public static Competition Create(
        string name, string category, CompetitionLevel level, int tier, string? group, string region, ExternalRef? externalRef)
    {
        var competition = new Competition { Id = NewId(), ExternalRef = externalRef };
        competition.UpdateDetails(name, category, level, tier, group, region);
        return competition;
    }

    public void UpdateDetails(string name, string category, CompetitionLevel level, int tier, string? group, string region)
    {
        if (!Enum.IsDefined(level))
            throw new DomainException(ErrorCode.CompetitionInvalidLevel);

        Name = Guard.NotEmpty(name, "nombre", 150);
        Category = Guard.NotEmpty(category, "categoría", 100);
        Level = level;
        Tier = Guard.InRange(tier, 0, 20, "escalón");
        Group = string.IsNullOrWhiteSpace(group) ? null : Guard.NotEmpty(group, "grupo", 20);
        Region = Guard.NotEmpty(region, "región", 100);
        SortOrder = (int)level * 1000 + Tier * 20 + (int.TryParse(Group, out var number) ? Math.Min(number, 19) : 0);
    }

    public Season AddSeason(string label, DateOnly startsOn, DateOnly endsOn, ExternalRef? externalRef)
    {
        if (endsOn <= startsOn)
            throw new DomainException(ErrorCode.CompetitionSeasonDates);

        if (_seasons.Any(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException(ErrorCode.CompetitionSeasonExists, label);

        var season = Season.Create(Id, label, startsOn, endsOn, externalRef);
        _seasons.Add(season);
        return season;
    }

    public Season? CurrentSeason => _seasons.FirstOrDefault(s => s.IsCurrent);

    public void MarkCurrent(Guid seasonId)
    {
        if (_seasons.All(s => s.Id != seasonId))
            throw new DomainException(ErrorCode.CompetitionSeasonMismatch);

        foreach (var season in _seasons)
            season.SetCurrent(season.Id == seasonId);
    }
}
