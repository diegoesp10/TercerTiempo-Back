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
        var save = new SaveInjuryTypeHandler(store, new NoUnitOfWork(), TimeProvider.System);

        await save.Handle(new SaveInjuryTypeCommand(null, "Rotura de menisco", 45, 5, true), default);
        var list = await save.Handle(new SaveInjuryTypeCommand(null, "Rotura de ménisco (otra)", null, 5, true), default);

        Assert.Contains(list, t => (t.Code, t.Label, t.AverageDays) == ("RoturaDeMenisco", "Rotura de menisco", 45));
        Assert.Contains(list, t => t.Code == "RoturaDeMeniscoOtra");
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => save.Handle(new SaveInjuryTypeCommand(null, "  ROTURA FIBRILAR ", 21, 1, true), default));
        Assert.Equal(ErrorCode.InjuryTypeDuplicate, duplicate.Code);
    }

    [Fact]
    public async Task Coaches_only_see_the_types_that_are_still_offered()
    {
        var retired = TestInjuryTypes.Of(InjuryTypeCodes.Contusion);
        retired.Update(retired.Label, retired.AverageDays, retired.SortOrder, active: false, TimeProvider.System.GetUtcNow());
        var store = new MemoryInjuryTypes(TestInjuryTypes.Of(InjuryTypeCodes.AnkleSprain), retired);

        var offered = await new ListInjuryTypesHandler(store).Handle(new ListInjuryTypesQuery(), default);
        var admin = await new ListAdminInjuryTypesHandler(store).Handle(new ListAdminInjuryTypesQuery(), default);

        Assert.Equal([InjuryTypeCodes.AnkleSprain], offered.Select(t => t.Type));
        Assert.Equal(2, admin.Count);
    }

    private sealed class MemoryInjuryTypes(params InjuryTypeDefinition[] seed) : IInjuryTypeStore
    {
        private readonly List<InjuryTypeDefinition> _types = [.. seed];
        public Dictionary<string, int> Usage { get; } = [];

        public Task<IReadOnlyList<InjuryTypeDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InjuryTypeDefinition>>(_types.ToList());

        public Task<InjuryTypeDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(_types.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(Usage);

        public void Add(InjuryTypeDefinition type) => _types.Add(type);

        public void Remove(InjuryTypeDefinition type) => _types.Remove(type);
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
