using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Tests;

public class InjuryTypeCatalogTests
{
    [Fact]
    public async Task A_type_some_injury_has_cannot_be_deleted_only_retired()
    {
        var store = new MemoryInjuryTypes(TestInjuryTypes.Of(InjuryTypeCodes.AnkleSprain), TestInjuryTypes.Of(InjuryTypeCodes.Contusion));
        store.Usage[InjuryTypeCodes.AnkleSprain] = 3;

        var refused = await Assert.ThrowsAsync<ConflictException>(() => new DeleteInjuryTypeHandler(store, new NoUnitOfWork())
            .Handle(new DeleteInjuryTypeCommand(InjuryTypeCodes.AnkleSprain), default));
        Assert.Equal((ErrorCode.InjuryTypeInUse, "Esguince de tobillo", 3), (refused.Code, refused.Args[0], refused.Args[1]));

        var left = await new DeleteInjuryTypeHandler(store, new NoUnitOfWork()).Handle(new DeleteInjuryTypeCommand(InjuryTypeCodes.Contusion), default);
        Assert.Equal([(InjuryTypeCodes.AnkleSprain, 3)], left.Select(t => (t.Code, t.Injuries)));
    }

    [Fact]
    public async Task A_new_type_gets_its_code_from_the_name_and_two_types_cannot_share_a_name()
    {
        var store = new MemoryInjuryTypes(TestInjuryTypes.Of(InjuryTypeCodes.MuscleTear));
        var save = new SaveInjuryTypeHandler(store, new MemoryBodySites(), new NoUnitOfWork(), TimeProvider.System);

        await save.Handle(new SaveInjuryTypeCommand(null, "Rotura de menisco", 45, 5, true), default);
        var list = await save.Handle(new SaveInjuryTypeCommand(null, "Rotura de ménisco (otra)", null, 5, true), default);

        Assert.Contains(list, t => (t.Code, t.Label, t.AverageDays) == ("RoturaDeMenisco", "Rotura de menisco", 45));
        Assert.Contains(list, t => t.Code == "RoturaDeMeniscoOtra");
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => save.Handle(new SaveInjuryTypeCommand(null, "  ROTURA FIBRILAR ", 21, 1, true), default));
        Assert.Equal(ErrorCode.InjuryTypeDuplicate, duplicate.Code);
    }

    [Fact]
    public async Task A_type_saves_what_it_asks_and_only_with_sites_that_fit()
    {
        var sites = TestInjuryCatalog.Sites();
        var store = new MemoryInjuryTypes();
        var save = new SaveInjuryTypeHandler(store, new MemoryBodySites([.. sites]), new NoUnitOfWork(), TimeProvider.System);

        // Un sitio fijo con lo que lo precisa (la clase sobra) y el grado.
        var list = await save.Handle(new SaveInjuryTypeCommand(
            null, "Lesión del ligamento cruzado", 240, 8, true, BodySiteKind.Ligament, "Knee", ["AnteriorCruciateLigament", "PosteriorCruciateLigament"], Graded: true), default);
        var cruciate = Assert.Single(list);
        Assert.Equal((null, "Knee", true), (cruciate.SiteKind, cruciate.BaseSite, cruciate.Graded));
        Assert.Equal(["AnteriorCruciateLigament", "PosteriorCruciateLigament"], cruciate.Sites);

        // Un hueso en una lista de músculos no encaja, ni un sitio que no existe; y el tipo no llega al catálogo.
        var wrongKind = await Assert.ThrowsAsync<DomainException>(() => save.Handle(
            new SaveInjuryTypeCommand(null, "Desgarro", 21, 4, true, BodySiteKind.Muscle, null, ["Femur"]), default));
        Assert.Equal(ErrorCode.InjuryTypeSitesInvalid, wrongKind.Code);
        var unknown = await Assert.ThrowsAsync<DomainException>(() => save.Handle(
            new SaveInjuryTypeCommand(null, "Desgarro", 21, 4, true, BodySiteKind.Muscle, null, ["Ala"]), default));
        Assert.Equal(ErrorCode.InjurySiteNotFound, unknown.Code);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Coaches_get_the_retired_types_marked_so_old_injuries_still_show_what_they_ask()
    {
        var retired = TestInjuryTypes.Of(InjuryTypeCodes.Contusion);
        retired.Update(retired.Label, retired.AverageDays, retired.SortOrder, active: false, TimeProvider.System.GetUtcNow());
        var store = new MemoryInjuryTypes(TestInjuryTypes.Of(InjuryTypeCodes.AnkleSprain), retired);

        var offered = await new ListInjuryTypesHandler(store).Handle(new ListInjuryTypesQuery(), default);
        var admin = await new ListAdminInjuryTypesHandler(store).Handle(new ListAdminInjuryTypesQuery(), default);

        Assert.Equal([(InjuryTypeCodes.AnkleSprain, true), (InjuryTypeCodes.Contusion, false)], offered.Select(t => (t.Type, t.Active)).OrderBy(t => t.Type));
        Assert.Equal(2, admin.Count);
    }
}
