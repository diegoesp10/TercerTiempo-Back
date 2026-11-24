using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Infrastructure.Persistence.Migrations;

namespace TercerTiempo.Application.Tests;

// El cuerpo y los tipos de lesión tal y como los siembra la migración: las reglas y las sugerencias se prueban con el
// catálogo con que nace la base de datos, no con uno inventado para la prueba.
internal static class TestInjuryCatalog
{
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

    public static List<BodySite> Sites() =>
    [
        .. InjuryAnatomy.Sites.Select((s, i) => BodySite.Create(
            s.Code, s.Label, Enum.Parse<BodySiteKind>(s.Kind), Enum.Parse<BodyRegion>(s.Region), s.Parent, s.Paired,
            Enum.Parse<BodySiteAgreement>(InjuryAnatomy.AgreementOf(s.Agreement)), (i + 1) * 10, SeededAt)),
    ];

    // Con lo que pregunta cada uno: si la migración nombrara un sitio que no existe o que no encaja, fallaría aquí.
    public static List<InjuryTypeDefinition> Types(IReadOnlyList<BodySite> sites)
    {
        var byCode = sites.ToDictionary(s => s.Code, StringComparer.Ordinal);
        return
        [
            .. InjuryAnatomy.Types.Select(t =>
            {
                var type = InjuryTypeDefinition.Create(t.Code, t.Label, t.Days, t.Order, SeededAt);
                type.SetLocation(
                    t.Kind is null ? null : Enum.Parse<BodySiteKind>(t.Kind), t.Base is null ? null : byCode[t.Base],
                    [.. t.Sites.Select(code => byCode[code])], t.Graded, SeededAt);
                return type;
            }),
        ];
    }
}

internal sealed class MemoryBodySites(params BodySite[] seed) : IBodySiteStore
{
    private readonly List<BodySite> _sites = [.. seed];
    public Dictionary<string, int> Usage { get; } = [];

    public Task<IReadOnlyList<BodySite>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BodySite>>(_sites.ToList());

    public Task<BodySite?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sites.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<string, int>> CountUsageAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(Usage);

    public void Add(BodySite site) => _sites.Add(site);

    public void Remove(BodySite site) => _sites.Remove(site);
}

internal sealed class MemoryInjuryTypes(params InjuryTypeDefinition[] seed) : IInjuryTypeStore
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

internal sealed class NoUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}
