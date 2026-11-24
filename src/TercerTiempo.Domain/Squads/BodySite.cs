using System.Globalization;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Squads;

// Lo que es cada sitio del cuerpo, y lo que pregunta un tipo de lesión: «¿Qué músculo?», «¿Qué hueso?»... Las
// articulaciones son también zonas del cuerpo: un golpe en la rodilla es un golpe en la rodilla.
public enum BodySiteKind
{
    Zone,
    Muscle,
    Bone,
    Tendon,
    Ligament,
    Joint,
    Cartilage,
}

// De la cabeza a los pies: agrupa los sitios al elegirlos y las lesiones al contarlas.
public enum BodyRegion
{
    Head,
    Neck,
    Shoulder,
    Arm,
    Elbow,
    Forearm,
    Wrist,
    Hand,
    Chest,
    Abdomen,
    Back,
    Hip,
    Thigh,
    Knee,
    Leg,
    Ankle,
    Foot,
}

// Cómo se escribe el lado con este sitio: «tobillo izquierdo», «rodilla izquierda», «isquiotibiales izquierdos», «dedos de
// la mano izquierda» (concuerda con la palabra a la que se refiere, no siempre la primera).
public enum BodySiteAgreement
{
    Masculine,
    Feminine,
    MasculinePlural,
    FemininePlural,
}

public enum BodySide
{
    Left,
    Right,
    Both,
}

// Un sitio del cuerpo donde puede estar una lesión: un músculo, un hueso, un tendón, un ligamento, una articulación o una
// zona. Es un catálogo que nace con la base de datos y luego mantiene el administrador.
public sealed class BodySite
{
    public const int CodeMaxLength = 40;
    public const int LabelMaxLength = 80;

    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public BodySiteKind Kind { get; private set; }
    public BodyRegion Region { get; private set; }
    // El grupo al que pertenece (el bíceps femoral, a los isquiotibiales): si no se sabe el músculo, se elige el grupo.
    public string? ParentCode { get; private set; }
    // Hay izquierdo y derecho: se pregunta el lado.
    public bool Paired { get; private set; }
    public BodySiteAgreement Agreement { get; private set; }
    public int SortOrder { get; private set; }
    public bool Active { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BodySite() { }

    public static BodySite Create(
        string code, string label, BodySiteKind kind, BodyRegion region, string? parentCode, bool paired, BodySiteAgreement agreement, int sortOrder,
        DateTimeOffset now)
    {
        var site = new BodySite { Code = Guard.NotEmpty(code, "código", CodeMaxLength), Active = true };
        site.Update(label, kind, region, parentCode, paired, agreement, sortOrder, active: true, now);
        return site;
    }

    public void Update(
        string label, BodySiteKind kind, BodyRegion region, string? parentCode, bool paired, BodySiteAgreement agreement, int sortOrder, bool active,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(region) || !Enum.IsDefined(agreement))
            throw new DomainException(ErrorCode.BodySiteInvalid);
        var parent = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim();
        if (parent is not null && parent == Code)
            throw new DomainException(ErrorCode.BodySiteParentInvalid);

        Label = Guard.NotEmpty(label, "nombre", LabelMaxLength);
        Kind = kind;
        Region = region;
        ParentCode = parent;
        Paired = paired;
        Agreement = agreement;
        SortOrder = sortOrder;
        Active = active;
        UpdatedAt = now;
    }

    private bool Feminine => Agreement is BodySiteAgreement.Feminine or BodySiteAgreement.FemininePlural;

    private bool Plural => Agreement is BodySiteAgreement.MasculinePlural or BodySiteAgreement.FemininePlural;

    // «izquierda», «derechos», «las dos»...
    public string SideWord(BodySide side) => side switch
    {
        BodySide.Left => Agree("izquierd"),
        BodySide.Right => Agree("derech"),
        _ => Plural ? "los dos lados" : Feminine ? "las dos" : "los dos",
    };

    private string Agree(string stem) => stem + (Feminine ? "a" : "o") + (Plural ? "s" : string.Empty);

    // «Rodilla izquierda», «Tobillo (los dos)». El lado va antes de lo que aclara el nombre entre paréntesis: «Escápula
    // izquierda (omóplato)».
    public string WithSide(BodySide? side)
    {
        if (side is not { } s || !Paired) return Label;
        var word = SideWord(s);
        var open = Label.EndsWith(')') ? Label.LastIndexOf(" (", StringComparison.Ordinal) : -1;
        if (s == BodySide.Both) return open > 0 ? $"{Label}, {word}" : $"{Label} ({word})";
        return open > 0 ? $"{Label[..open]} {word}{Label[open..]}" : $"{Label} {word}";
    }

    public static string Lower(string text) =>
        text.Length == 0 ? text : char.ToLower(text[0], CultureInfo.GetCultureInfo("es-ES")) + text[1..];
}

public interface IBodySiteStore
{
    Task<IReadOnlyList<BodySite>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BodySite?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    // Lesiones guardadas en cada sitio: con alguna no se borra.
    Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default);
    void Add(BodySite site);
    void Remove(BodySite site);
}
