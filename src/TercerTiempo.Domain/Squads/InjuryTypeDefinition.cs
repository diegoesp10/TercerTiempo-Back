using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

// Un tipo de lesión del catálogo que mantiene el administrador: su nombre, la duración media orientativa con la que se
// propone la vuelta al registrar una lesión y lo que se pregunta de ella (dónde fue en el cuerpo y el grado). Un tipo que
// ya usa alguna lesión no se borra (la base de datos tampoco lo deja): se retira, y deja de ofrecerse para las nuevas sin
// tocar las que ya lo tienen.
public sealed class InjuryTypeDefinition
{
    public const int CodeMaxLength = 30;
    public const int LabelMaxLength = 60;
    public const int MaxAverageDays = 730;
    public const int MaxSites = 60;

    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public int? AverageDays { get; private set; }
    public int SortOrder { get; private set; }
    public bool Active { get; private set; }
    // Dónde fue: o un sitio fijo (el tobillo, en el esguince de tobillo) con lo que lo precisa si se sabe (sus ligamentos,
    // en SiteCodes), o cualquier sitio de una clase (cualquier músculo, en la contractura), toda o solo los de SiteCodes.
    // Sin ninguno de los dos no se pregunta (una enfermedad).
    public BodySiteKind? SiteKind { get; private set; }
    public string? BaseSiteCode { get; private set; }
    public BodySite? BaseSite { get; private set; }
    public IReadOnlyList<string> SiteCodes { get; private set; } = [];
    // Se pregunta el grado: I distensión, II rotura parcial, III rotura completa.
    public bool Graded { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool AsksSite => BaseSiteCode is not null || SiteKind is not null;

    private InjuryTypeDefinition() { }

    public static InjuryTypeDefinition Create(string code, string label, int? averageDays, int sortOrder, DateTimeOffset now)
    {
        var type = new InjuryTypeDefinition { Code = Guard.NotEmpty(code, "código", CodeMaxLength), Active = true };
        type.Update(label, averageDays, sortOrder, active: true, now);
        return type;
    }

    public void Update(string label, int? averageDays, int sortOrder, bool active, DateTimeOffset now)
    {
        if (averageDays is { } days && (days < 1 || days > MaxAverageDays))
            throw new DomainException(ErrorCode.InjuryTypeAverageDaysInvalid, MaxAverageDays);

        Label = Guard.NotEmpty(label, "nombre", LabelMaxLength);
        AverageDays = averageDays;
        SortOrder = sortOrder;
        Active = active;
        UpdatedAt = now;
    }

    // Lo que se pregunta al registrar una lesión de este tipo. Con un sitio fijo, la clase sobra: lo que lo precisa va en
    // la lista. Sin sitio fijo ni clase no hay lista.
    public void SetLocation(BodySiteKind? kind, BodySite? baseSite, IReadOnlyList<BodySite> sites, bool graded, DateTimeOffset now)
    {
        if (kind is { } asked && !Enum.IsDefined(asked))
            throw new DomainException(ErrorCode.InjuryTypeSitesInvalid, asked);
        if (baseSite is not null)
            kind = null;
        if (sites.Count > MaxSites)
            throw new DomainException(ErrorCode.InjuryTypeSitesTooMany, MaxSites);

        foreach (var site in sites)
        {
            var fits = baseSite is not null ? site.Code != baseSite.Code : kind is { } k && Fits(k, site.Kind);
            if (!fits)
                throw new DomainException(ErrorCode.InjuryTypeSitesInvalid, site.Label);
        }

        SiteKind = kind;
        BaseSiteCode = baseSite?.Code;
        BaseSite = baseSite;
        SiteCodes = sites.Select(s => s.Code).Distinct(StringComparer.Ordinal).ToList();
        Graded = graded;
        UpdatedAt = now;
    }

    // Lo que se pregunta por zona del cuerpo incluye las articulaciones: un golpe en la rodilla.
    public static bool Fits(BodySiteKind asked, BodySiteKind kind) => kind == asked || (asked == BodySiteKind.Zone && kind == BodySiteKind.Joint);

    public bool Allows(BodySite site) => BaseSiteCode is { } fixedSite
        ? site.Code == fixedSite || SiteCodes.Contains(site.Code)
        : SiteKind is { } kind && (SiteCodes.Count > 0 ? SiteCodes.Contains(site.Code) : Fits(kind, site.Kind));

    // Para decir qué falta: «qué músculo», «qué hueso»...
    public string SiteQuestion() => SiteKind switch
    {
        BodySiteKind.Muscle => "qué músculo",
        BodySiteKind.Bone => "qué hueso",
        BodySiteKind.Tendon => "qué tendón",
        BodySiteKind.Ligament => "qué ligamento",
        BodySiteKind.Joint => "qué articulación",
        BodySiteKind.Cartilage => "qué cartílago",
        _ => "en qué zona del cuerpo fue",
    };
}

// Los tipos con los que nació el catálogo (antes fijos en el código, sus códigos siguen en las lesiones ya guardadas) y los
// que llegaron con la anatomía (24-11-2026).
public static class InjuryTypeCodes
{
    public const string MuscleContracture = "MuscleContracture";
    public const string MuscleTear = "MuscleTear";
    public const string AnkleSprain = "AnkleSprain";
    public const string KneeSprain = "KneeSprain";
    public const string CruciateLigament = "CruciateLigament";
    public const string Meniscus = "Meniscus";
    public const string Fracture = "Fracture";
    public const string Contusion = "Contusion";
    public const string Tendinitis = "Tendinitis";
    public const string Pubalgia = "Pubalgia";
    public const string Concussion = "Concussion";
    public const string Illness = "Illness";
    public const string Other = "Other";

    public const string MuscleOverload = "MuscleOverload";
    public const string MuscleStrain = "MuscleStrain";
    public const string Sprain = "Sprain";
    public const string Dislocation = "Dislocation";
    public const string StressFracture = "StressFracture";
    public const string Laceration = "Laceration";
    public const string TendonRupture = "TendonRupture";
    public const string Periostitis = "Periostitis";
    public const string PlantarFasciitis = "PlantarFasciitis";
    public const string LowBackPain = "LowBackPain";
}

public interface IInjuryTypeStore
{
    Task<IReadOnlyList<InjuryTypeDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<InjuryTypeDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default);
    void Add(InjuryTypeDefinition type);
    void Remove(InjuryTypeDefinition type);
}
