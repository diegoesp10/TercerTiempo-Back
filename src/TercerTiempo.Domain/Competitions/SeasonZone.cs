using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Competitions;

// Qué se juega cada puesto de la clasificación final de una liga.
public enum LeagueZoneKind
{
    Champion = 1,
    DirectPromotion = 2,
    PromotionPlayoff = 3,
    RelegationPlayoff = 4,
    Relegation = 5,
    Other = 9,
}

// Un puesto destacado de la clasificación tal como lo publica la web de la liga (en futbolasturiano.es, el color de la
// posición y su leyenda): «Ascenso directo», «Promoción de ascenso», «Promoción por la permanencia», «Descenso»... Cada
// liga tiene su formato y puede cambiar de una temporada a otra, así que va por temporada.
public sealed class SeasonZone
{
    public const int MaxLabelLength = 80;

    public Guid SeasonId { get; private set; }
    public int Position { get; private set; }
    public LeagueZoneKind Kind { get; private set; }
    public string Label { get; private set; } = string.Empty;
    // Color #rrggbb con el que la web marca el puesto.
    public string Color { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private SeasonZone() { }

    public static SeasonZone Create(Guid seasonId, LeagueZoneData data, DateTimeOffset now) => new()
    {
        SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
        Position = Guard.InRange(data.Position, 1, 60, "puesto"),
        Kind = Classify(data.Label),
        Label = Guard.NotEmpty(data.Label.Trim(), "plaza", MaxLabelLength),
        Color = Guard.NotEmpty(data.Color.Trim().ToLowerInvariant(), "color", 7),
        UpdatedAt = now,
    };

    public bool Matches(LeagueZoneData data) =>
        Position == data.Position && Label == data.Label.Trim() && Color == data.Color.Trim().ToLowerInvariant();

    public void Update(LeagueZoneData data, DateTimeOffset now)
    {
        Kind = Classify(data.Label);
        Label = Guard.NotEmpty(data.Label.Trim(), "plaza", MaxLabelLength);
        Color = Guard.NotEmpty(data.Color.Trim().ToLowerInvariant(), "color", 7);
        UpdatedAt = now;
    }

    // Por el texto de la leyenda: «Campeón», «Ascenso directo», «Promoción de ascenso», «Promoción por la permanencia» o
    // «de descenso», «Descenso».
    public static LeagueZoneKind Classify(string label)
    {
        var text = NameNormalizer.Normalize(label);
        if (text.Contains("campeon", StringComparison.Ordinal)) return LeagueZoneKind.Champion;
        if (text.Contains("permanencia", StringComparison.Ordinal)
            || (text.Contains("promocion", StringComparison.Ordinal) && text.Contains("descenso", StringComparison.Ordinal)))
            return LeagueZoneKind.RelegationPlayoff;
        if (text.Contains("descenso", StringComparison.Ordinal)) return LeagueZoneKind.Relegation;
        if (text.Contains("promocion", StringComparison.Ordinal)) return LeagueZoneKind.PromotionPlayoff;
        if (text.Contains("ascenso", StringComparison.Ordinal)) return LeagueZoneKind.DirectPromotion;
        return LeagueZoneKind.Other;
    }

    public static bool IsPromotion(LeagueZoneKind kind) =>
        kind is LeagueZoneKind.Champion or LeagueZoneKind.DirectPromotion or LeagueZoneKind.PromotionPlayoff;
}

// Un puesto destacado tal como lo publica la web: posición, leyenda y color.
public sealed record LeagueZoneData(int Position, string Label, string Color);

public interface ISeasonZoneStore
{
    Task<IReadOnlyList<SeasonZone>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);
    void Add(SeasonZone zone);
    void Remove(SeasonZone zone);
}
