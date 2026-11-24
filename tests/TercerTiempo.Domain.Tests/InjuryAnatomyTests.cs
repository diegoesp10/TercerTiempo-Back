using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

// Dónde fue la lesión: lo que pregunta su tipo (el músculo, el hueso, qué rodilla...), el lado si el sitio tiene dos, el
// grado si el tipo lo lleva y cómo se produjo.
public class InjuryAnatomyTests
{
    // Por prueba (xUnit crea la clase en cada una): las que retiran un sitio no tocan las demás.
    private readonly BodySite _knee = Site("Knee", "Rodilla", BodySiteKind.Joint, BodyRegion.Knee, BodySiteAgreement.Feminine);
    private readonly BodySite _acl = Site("AnteriorCruciateLigament", "Ligamento cruzado anterior (LCA)", BodySiteKind.Ligament, BodyRegion.Knee);
    private readonly BodySite _pcl = Site("PosteriorCruciateLigament", "Ligamento cruzado posterior (LCP)", BodySiteKind.Ligament, BodyRegion.Knee);
    private readonly BodySite _ankle = Site("Ankle", "Tobillo", BodySiteKind.Joint, BodyRegion.Ankle);
    private readonly BodySite _hamstrings = Site("Hamstrings", "Isquiotibiales", BodySiteKind.Muscle, BodyRegion.Thigh, BodySiteAgreement.MasculinePlural);
    private readonly BodySite _bicepsFemoris = Site("BicepsFemoris", "Bíceps femoral", BodySiteKind.Muscle, BodyRegion.Thigh, parent: "Hamstrings");
    private readonly BodySite _femur = Site("Femur", "Fémur", BodySiteKind.Bone, BodyRegion.Thigh);
    private readonly BodySite _sternum = Site("Sternum", "Esternón", BodySiteKind.Bone, BodyRegion.Chest, paired: false);
    private readonly BodySite _ribs = Site("Ribs", "Costillas", BodySiteKind.Bone, BodyRegion.Chest, BodySiteAgreement.FemininePlural);
    private readonly BodySite _scapula = Site("Scapula", "Escápula (omóplato)", BodySiteKind.Bone, BodyRegion.Shoulder, BodySiteAgreement.Feminine);

    private static BodySite Site(
        string code, string label, BodySiteKind kind, BodyRegion region, BodySiteAgreement agreement = BodySiteAgreement.Masculine, bool paired = true,
        string? parent = null) =>
        BodySite.Create(code, label, kind, region, parent, paired, agreement, 0, TestData.Now);

    private static InjuryTypeDefinition Type(string code, BodySiteKind? kind = null, BodySite? baseSite = null, BodySite[]? sites = null, bool graded = false)
    {
        var type = TestData.InjuryType(code);
        type.SetLocation(kind, baseSite, sites ?? [], graded, TestData.Now);
        return type;
    }

    private static void Retire(BodySite site) =>
        site.Update(site.Label, site.Kind, site.Region, site.ParentCode, site.Paired, site.Agreement, site.SortOrder, active: false, TestData.Now);

    private static (Squad Squad, SquadEditor Editor, SquadPlayer Pelayo) SquadWithPelayo()
    {
        var (squad, coachId) = TestData.NewSquad();
        return (squad, SquadEditor.Coach(coachId), squad.AddPlayer("Pelayo Fernández", Position.Forward, null, null, TestData.Now));
    }

    private static PlayerInjury Record(InjuryTypeDefinition type, InjuryDetail? detail)
    {
        var (squad, editor, pelayo) = SquadWithPelayo();
        return PlayerInjury.Create(squad, pelayo, editor, type, null, TestData.Today, null, TestData.Now, detail: detail);
    }

    private static string Fails(Action action) => Assert.Throws<DomainException>(action).Code;

    [Fact]
    public void A_contracture_asks_which_muscle_and_only_takes_muscles()
    {
        var contracture = Type(InjuryTypeCodes.MuscleContracture, BodySiteKind.Muscle);

        Assert.Equal(ErrorCode.InjurySiteRequired, Fails(() => Record(contracture, null)));
        Assert.Equal(ErrorCode.InjurySiteInvalid, Fails(() => Record(contracture, new InjuryDetail(_femur, BodySide.Left))));
        Assert.Equal(ErrorCode.InjurySideRequired, Fails(() => Record(contracture, new InjuryDetail(_bicepsFemoris))));

        var injury = Record(contracture, new InjuryDetail(_bicepsFemoris, BodySide.Right, InjuryGrade.II, InjuryMechanism.NonContact));

        Assert.Equal(("BicepsFemoris", BodySide.Right, "Bíceps femoral derecho"), (injury.SiteCode, injury.Side, injury.Location));
        // La contractura no lleva grado; cómo se produjo, sí.
        Assert.Null(injury.Grade);
        Assert.Equal(InjuryMechanism.NonContact, injury.Mechanism);
    }

    [Fact]
    public void A_cruciate_ligament_asks_which_knee_and_which_ligament_if_known()
    {
        var cruciate = Type(InjuryTypeCodes.CruciateLigament, baseSite: _knee, sites: [_acl, _pcl], graded: true);

        // Sin precisar, la rodilla del tipo; pero hay dos, así que el lado hace falta.
        Assert.Equal(ErrorCode.InjurySideRequired, Fails(() => Record(cruciate, null)));
        var knee = Record(cruciate, new InjuryDetail(Side: BodySide.Left));
        Assert.Equal(("Knee", "Rodilla izquierda"), (knee.SiteCode, knee.Location));

        var precise = Record(cruciate, new InjuryDetail(_acl, BodySide.Right, InjuryGrade.III, InjuryMechanism.NonContact));
        Assert.Equal("Ligamento cruzado anterior (LCA), rodilla derecha", precise.Location);
        Assert.Equal((BodySide.Right, InjuryGrade.III), (precise.Side, precise.Grade));

        // Ni otro sitio que no lo precise.
        Assert.Equal(ErrorCode.InjurySiteInvalid, Fails(() => Record(cruciate, new InjuryDetail(_ankle, BodySide.Left))));
    }

    [Fact]
    public void A_site_without_two_sides_keeps_no_side_and_a_type_that_asks_nothing_keeps_no_site()
    {
        var sternum = Record(Type(InjuryTypeCodes.Fracture, BodySiteKind.Bone), new InjuryDetail(_sternum, BodySide.Left));
        Assert.Null(sternum.Side);
        Assert.Equal("Esternón", sternum.Location);

        var flu = Record(Type(InjuryTypeCodes.Illness), new InjuryDetail(_femur, BodySide.Left, InjuryGrade.I));
        Assert.Null(flu.SiteCode);
        Assert.Null(flu.Side);
        Assert.Null(flu.Grade);
        Assert.Null(flu.Location);
    }

    [Fact]
    public void A_retired_site_is_not_offered_for_new_injuries_but_the_one_that_has_it_keeps_it()
    {
        var fracture = Type(InjuryTypeCodes.Fracture, BodySiteKind.Bone);
        var injury = Record(fracture, new InjuryDetail(_scapula, BodySide.Left));

        Retire(_scapula);

        injury.Update(fracture, "Caída", TestData.Today, TestData.Today.AddDays(40), TestData.Now, detail: new InjuryDetail(_scapula, BodySide.Right));
        Assert.Equal("Escápula derecha (omóplato)", injury.Location);
        Assert.Equal(ErrorCode.InjurySiteRetired, Fails(() => Record(fracture, new InjuryDetail(_scapula, BodySide.Left))));
    }

    [Fact]
    public void The_fixed_site_of_a_type_keeps_serving_while_the_type_is_offered()
    {
        var sprain = Type(InjuryTypeCodes.AnkleSprain, baseSite: _ankle, graded: true);
        Retire(_ankle);

        var injury = Record(sprain, new InjuryDetail(Side: BodySide.Both, Grade: InjuryGrade.I));

        Assert.Equal(("Tobillo (los dos)", InjuryGrade.I), (injury.Location, injury.Grade));
    }

    [Fact]
    public void A_relapse_is_where_the_first_one_was_unless_it_says_otherwise()
    {
        var (squad, editor, pelayo) = SquadWithPelayo();
        var tear = Type(InjuryTypeCodes.MuscleTear, BodySiteKind.Muscle, graded: true);
        var first = PlayerInjury.Create(squad, pelayo, editor, tear, null, TestData.Today.AddDays(-40), null, TestData.Now,
            detail: new InjuryDetail(_bicepsFemoris, BodySide.Left, InjuryGrade.II, InjuryMechanism.NonContact));
        first.Close(TestData.Today.AddDays(-10), TestData.Now);

        var relapse = PlayerInjury.Create(squad, pelayo, editor, tear, null, TestData.Today, null, TestData.Now, relapseOf: first);
        Assert.Equal(("BicepsFemoris", BodySide.Left, InjuryGrade.II), (relapse.SiteCode, relapse.Side, relapse.Grade));

        // Corregirla sin decir dónde no lo borra; decirlo, lo cambia.
        relapse.Update(tear, "Molestias", TestData.Today, TestData.Today.AddDays(20), TestData.Now);
        Assert.Equal("Bíceps femoral izquierdo", relapse.Location);
        relapse.Update(tear, "Molestias", TestData.Today, TestData.Today.AddDays(20), TestData.Now, detail: new InjuryDetail(_hamstrings, BodySide.Both));
        Assert.Equal(("Isquiotibiales (los dos lados)", null), (relapse.Location, relapse.Grade));
    }

    [Fact]
    public void The_side_agrees_with_the_site()
    {
        Assert.Equal("Rodilla izquierda", _knee.WithSide(BodySide.Left));
        Assert.Equal("Tobillo derecho", _ankle.WithSide(BodySide.Right));
        Assert.Equal("Isquiotibiales izquierdos", _hamstrings.WithSide(BodySide.Left));
        Assert.Equal("Costillas derechas", _ribs.WithSide(BodySide.Right));
        Assert.Equal("Escápula izquierda (omóplato)", _scapula.WithSide(BodySide.Left));
        Assert.Equal("Rodilla (las dos)", _knee.WithSide(BodySide.Both));
        Assert.Equal("Tobillo (los dos)", _ankle.WithSide(BodySide.Both));
        Assert.Equal("Costillas (los dos lados)", _ribs.WithSide(BodySide.Both));
        Assert.Equal("Escápula (omóplato), las dos", _scapula.WithSide(BodySide.Both));
        Assert.Equal("Esternón", _sternum.WithSide(BodySide.Left));
    }

    [Fact]
    public void A_type_only_lists_sites_that_refine_its_fixed_site_or_are_of_its_kind()
    {
        var type = TestData.InjuryType(InjuryTypeCodes.Sprain);

        Assert.Equal(ErrorCode.InjuryTypeSitesInvalid, Fails(() => type.SetLocation(null, _knee, [_knee], graded: true, TestData.Now)));
        Assert.Equal(ErrorCode.InjuryTypeSitesInvalid, Fails(() => type.SetLocation(BodySiteKind.Muscle, null, [_femur], graded: false, TestData.Now)));
        Assert.Equal(ErrorCode.InjuryTypeSitesInvalid, Fails(() => type.SetLocation(null, null, [_femur], graded: false, TestData.Now)));

        // Por zona del cuerpo valen también las articulaciones (un golpe en la rodilla)...
        type.SetLocation(BodySiteKind.Zone, null, [_knee], graded: false, TestData.Now);
        Assert.True(type.Allows(_knee));
        Assert.False(type.Allows(_ankle));
        // ...y con un sitio fijo la clase sobra.
        type.SetLocation(BodySiteKind.Ligament, _knee, [_acl], graded: true, TestData.Now);
        Assert.Null(type.SiteKind);
        Assert.Equal("Knee", type.BaseSiteCode);
        Assert.Equal(["AnteriorCruciateLigament"], type.SiteCodes);
        Assert.True(type.Allows(_knee) && type.Allows(_acl) && !type.Allows(_pcl));
    }
}
