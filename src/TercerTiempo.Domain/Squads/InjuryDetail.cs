namespace TercerTiempo.Domain.Squads;

// Grado de la lesión de un ligamento o de un músculo: I distensión, II rotura parcial, III rotura completa.
public enum InjuryGrade
{
    I = 1,
    II = 2,
    III = 3,
}

// Cómo se produjo: en un choque o una entrada, sin que nadie le tocara (al correr, girar o saltar) o por sobrecarga, una
// molestia que fue a más.
public enum InjuryMechanism
{
    Contact,
    NonContact,
    Overuse,
}

// Lo que precisa una lesión además del tipo: dónde fue en el cuerpo, el lado, el grado y cómo se produjo.
public sealed record InjuryDetail(BodySite? Site = null, BodySide? Side = null, InjuryGrade? Grade = null, InjuryMechanism? Mechanism = null)
{
    public static readonly InjuryDetail None = new();
}

public static class InjuryLocation
{
    // El que tiene el lado: lo elegido o, si lo elegido precisa el sitio fijo del tipo (el ligamento cruzado anterior, en
    // la rodilla), ese sitio.
    public static BodySite? SideSite(InjuryTypeDefinition type, BodySite? site) =>
        site is null ? null : type.BaseSite is { } fixedSite && fixedSite.Code != site.Code ? fixedSite : site;

    // «Bíceps femoral izquierdo», «Menisco interno, rodilla derecha», «Tobillo (los dos)». Sin sitio, nada: las lesiones
    // de antes de que se preguntara.
    public static string? Describe(InjuryTypeDefinition type, BodySite? site, BodySide? side)
    {
        if (site is null) return null;
        var sided = SideSite(type, site)!;
        return sided.Code == site.Code ? site.WithSide(side) : $"{site.Label}, {BodySite.Lower(sided.WithSide(side))}";
    }
}
