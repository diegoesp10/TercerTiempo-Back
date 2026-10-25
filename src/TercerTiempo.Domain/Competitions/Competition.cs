using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Competitions;

public sealed class Competition : AggregateRoot
{
    private readonly List<Season> _seasons = [];

    public string Name { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public CompetitionLevel Level { get; private set; }

    public int Tier { get; private set; }

    public string? Group { get; private set; }

    public string Region { get; private set; } = string.Empty;

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

    public Season AddSeason(string label, DateOnly startsOn, DateOnly endsOn, ExternalRef? externalRef, Guid seasonYearId = default)
    {
        if (endsOn <= startsOn)
            throw new DomainException(ErrorCode.CompetitionSeasonDates);

        if (_seasons.Any(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException(ErrorCode.CompetitionSeasonExists, label);

        var season = Season.Create(Id, seasonYearId, label, startsOn, endsOn, externalRef);
        _seasons.Add(season);
        return season;
    }

    public Season? CurrentSeason => _seasons.FirstOrDefault(s => s.IsCurrent);

    // La temporada de esta liga dentro de la temporada de la app; null si la liga no se jugó (o no se sigue) ese año.
    public Season? SeasonIn(Guid seasonYearId) => _seasons.FirstOrDefault(s => s.SeasonYearId == seasonYearId);

    // Una temporada creada antes de que existiera la temporada de la app se cuelga de ella al sincronizar.
    public void LinkSeasonYear(Guid seasonId, Guid seasonYearId)
    {
        var season = _seasons.FirstOrDefault(s => s.Id == seasonId) ?? throw new DomainException(ErrorCode.CompetitionSeasonMismatch);
        season.MoveTo(seasonYearId);
    }

    public void MarkCurrent(Guid seasonId)
    {
        if (_seasons.All(s => s.Id != seasonId))
            throw new DomainException(ErrorCode.CompetitionSeasonMismatch);

        foreach (var season in _seasons)
            season.SetCurrent(season.Id == seasonId);
    }
}
