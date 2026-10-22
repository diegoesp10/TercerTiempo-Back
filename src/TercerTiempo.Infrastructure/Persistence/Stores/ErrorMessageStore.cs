using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Infrastructure.Persistence.Stores;

internal sealed class ErrorMessageStore(AppDbContext db, IMemoryCache cache) : IErrorMessageStore
{
    private const string CacheKey = "error-messages";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyDictionary<(string Code, string Language), string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<(string, string), string>? cached) && cached is not null)
            return cached;

        var rows = await db.ErrorMessages.AsNoTracking().ToListAsync(cancellationToken);
        var map = rows.ToDictionary(r => (r.Code, r.Language), r => r.Message);

        cache.Set(CacheKey, (IReadOnlyDictionary<(string, string), string>)map, CacheFor);
        return map;
    }
}
