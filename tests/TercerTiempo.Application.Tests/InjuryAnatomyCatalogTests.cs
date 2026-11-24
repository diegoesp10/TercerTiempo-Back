using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Tests;

// El cuerpo con que nace la base de datos, dónde fue la lesión según la crónica y lo que deja hacer el administrador con los
// sitios.
public class InjuryAnatomyCatalogTests
{
    private static readonly List<BodySite> Sites = TestInjuryCatalog.Sites();
    private static readonly List<InjuryTypeDefinition> Types = TestInjuryCatalog.Types(Sites);

    private static (string? Site, BodySide? Side) Suggest(string type, string phrase)
    {
        var (site, side) = InjuryLocationSuggester.Suggest(phrase, Types.Single(t => t.Code == type), Sites);
        return (site?.Code, side);
    }

    [Fact]
    public void The_catalog_the_database_is_born_with_is_consistent()
    {
        Assert.Equal(Sites.Count, Sites.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Sites.Count, Sites.Select(s => NameNormalizer.Normalize(s.Label)).Distinct().Count());
        var byCode = Sites.ToDictionary(s => s.Code);
        foreach (var site in Sites.Where(s => s.ParentCode is not null))
        {
            var group = byCode[site.ParentCode!];
            Assert.Equal(site.Kind, group.Kind);
            Assert.Null(group.ParentCode);
        }

        // Todos los tipos preguntan algo menos la enfermedad, y los de siempre siguen con su código.
        Assert.Equal([InjuryTypeCodes.Illness], Types.Where(t => !t.AsksSite).Select(t => t.Code));
        Assert.Equal(Types.Count, Types.Select(t => t.Code).Distinct().Count());
        Assert.Contains(Types, t => t.Code == InjuryTypeCodes.CruciateLigament && t.BaseSiteCode == "Knee" && t.Graded);
    }

    [Theory]
    [InlineData("AnkleSprain", "sufrió un esguince de tobillo derecho", "Ankle", BodySide.Right)]
    [InlineData("AnkleSprain", "esguince en el ligamento peroneoastragalino del tobillo izquierdo", "AnteriorTalofibularLigament", BodySide.Left)]
    [InlineData("AnkleSprain", "se torció el tobillo izquierdo y después el derecho", "Ankle", null)]
    [InlineData("CruciateLigament", "rotura del ligamento cruzado anterior de la rodilla izquierda", "AnteriorCruciateLigament", BodySide.Left)]
    [InlineData("Meniscus", "lesión en el menisco interno", "MedialMeniscus", null)]
    [InlineData("MuscleTear", "notó un pinchazo en el gemelo", "TricepsSurae", null)]
    [InlineData("MuscleTear", "rotura fibrilar en el isquio", "Hamstrings", null)]
    [InlineData("MuscleContracture", "contractura en el bíceps femoral derecho", "BicepsFemoris", BodySide.Right)]
    [InlineData("Fracture", "fractura del quinto metatarsiano", "FifthMetatarsal", null)]
    [InlineData("Tendinitis", "arrastra una tendinitis rotuliana", "PatellarTendon", null)]
    public void It_suggests_the_site_the_chronicle_names(string type, string phrase, string site, BodySide? side) =>
        Assert.Equal((site, side), Suggest(type, phrase));

    [Theory]
    [InlineData("Contusion", "un golpe en la pierna")]
    [InlineData("Contusion", "golpe en la rodilla izquierda y en el tobillo derecho")]
    [InlineData("MuscleTear", "se retiró con molestias")]
    [InlineData("Fracture", "le hicieron una radiografía")]
    public void Without_a_clear_site_it_suggests_none(string type, string phrase)
    {
        // Ni el pie por la pierna, ni uno de dos sitios, ni el radio por la radiografía: mejor nada que un sitio falso.
        var (site, side) = Suggest(type, phrase);
        Assert.Null(site);
        Assert.Null(side);
    }

    [Fact]
    public async Task The_site_comes_from_the_catalog_by_its_code()
    {
        var store = new MemoryBodySites([.. Sites]);

        var detail = await BodySiteCatalog.DetailAsync(store, " Hamstrings ", BodySide.Left, InjuryGrade.II, InjuryMechanism.NonContact, default);
        Assert.Equal(("Hamstrings", BodySide.Left, InjuryGrade.II), (detail.Site?.Code, detail.Side, detail.Grade));

        var unknown = await Assert.ThrowsAsync<DomainException>(() => BodySiteCatalog.DetailAsync(store, "Ala", null, null, null, default));
        Assert.Equal(ErrorCode.InjurySiteNotFound, unknown.Code);
    }

    [Fact]
    public async Task A_group_is_a_site_of_the_same_kind_that_is_not_inside_another()
    {
        var store = new MemoryBodySites([.. TestInjuryCatalog.Sites()]);
        var save = new SaveBodySiteHandler(store, new MemoryInjuryTypes(), new NoUnitOfWork(), TimeProvider.System);
        SaveBodySiteCommand Muscle(string? code, string label, string? parent) =>
            new(code, label, BodySiteKind.Muscle, BodyRegion.Thigh, parent, true, BodySiteAgreement.Masculine, 0, true);

        // En un hueso, en un músculo que ya está en un grupo, o un grupo dentro de otro: no.
        Assert.Equal(ErrorCode.BodySiteParentInvalid, (await Assert.ThrowsAsync<DomainException>(() => save.Handle(Muscle(null, "Vasto oblicuo", "Femur"), default))).Code);
        Assert.Equal(ErrorCode.BodySiteParentInvalid, (await Assert.ThrowsAsync<DomainException>(() => save.Handle(Muscle(null, "Vasto oblicuo", "VastusMedialis"), default))).Code);
        Assert.Equal(ErrorCode.BodySiteParentInvalid, (await Assert.ThrowsAsync<DomainException>(() => save.Handle(Muscle("Hamstrings", "Isquiotibiales", "Quadriceps"), default))).Code);
        // Ni un grupo con los suyos cambia de clase.
        Assert.Equal(ErrorCode.BodySiteParentInvalid, (await Assert.ThrowsAsync<DomainException>(() => save.Handle(
            new SaveBodySiteCommand("Hamstrings", "Isquiotibiales", BodySiteKind.Tendon, BodyRegion.Thigh, null, true, BodySiteAgreement.MasculinePlural, 0, true), default))).Code);
        // Dos sitios no se llaman igual.
        Assert.Equal(ErrorCode.BodySiteDuplicate, (await Assert.ThrowsAsync<ConflictException>(() => save.Handle(Muscle(null, " ISQUIOTIBIALES ", null), default))).Code);

        var list = await save.Handle(Muscle(null, "Vasto oblicuo", "Quadriceps"), default);
        var added = Assert.Single(list, s => s.Label == "Vasto oblicuo");
        Assert.Equal(("VastoOblicuo", "Quadriceps"), (added.Code, added.Parent));
        Assert.Equal(5, Assert.Single(list, s => s.Code == "Quadriceps").Children);
    }

    [Fact]
    public async Task A_site_something_uses_cannot_be_deleted_and_it_says_what_uses_it()
    {
        var sites = TestInjuryCatalog.Sites();
        var store = new MemoryBodySites([.. sites]);
        store.Usage["Hamstrings"] = 3;
        var delete = new DeleteBodySiteHandler(store, new MemoryInjuryTypes([.. TestInjuryCatalog.Types(sites)]), new NoUnitOfWork());

        var hamstrings = await Assert.ThrowsAsync<ConflictException>(() => delete.Handle(new DeleteBodySiteCommand("Hamstrings"), default));
        Assert.Equal((ErrorCode.BodySiteInUse, "Isquiotibiales", "3 lesiones y 3 sitios de su grupo"), (hamstrings.Code, hamstrings.Args[0], hamstrings.Args[1]));
        var knee = await Assert.ThrowsAsync<ConflictException>(() => delete.Handle(new DeleteBodySiteCommand("Knee"), default));
        // El esguince de rodilla, el ligamento cruzado y el menisco.
        Assert.Equal("3 tipos de lesión", knee.Args[1]);

        var left = await delete.Handle(new DeleteBodySiteCommand("Coccyx"), default);
        Assert.DoesNotContain(left, s => s.Code == "Coccyx");
        Assert.Equal(sites.Count - 1, left.Count);
    }
}
