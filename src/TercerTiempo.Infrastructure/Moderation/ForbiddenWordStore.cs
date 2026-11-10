using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Caching.Memory;
using TercerTiempo.Domain.Moderation;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Moderation;

internal sealed class ForbiddenWordStore(AppDbContext db, IMemoryCache cache) : IForbiddenWordStore
{
    private const string CacheKey = "forbidden-words";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        IReadOnlyList<string> terms = await db.Set<ForbiddenWord>().AsNoTracking().OrderBy(w => w.Term).Select(w => w.Term).ToListAsync(cancellationToken);
        cache.Set(CacheKey, terms, CacheFor);
        return terms;
    }

    public async Task<IReadOnlyList<ForbiddenWord>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Set<ForbiddenWord>().AsNoTracking().OrderBy(w => w.Term).ToListAsync(cancellationToken);

    public Task<ForbiddenWord?> GetAsync(string term, CancellationToken cancellationToken = default) =>
        db.Set<ForbiddenWord>().FirstOrDefaultAsync(w => w.Term == term, cancellationToken);

    public void Add(ForbiddenWord word) => db.Set<ForbiddenWord>().Add(word);

    public void Remove(ForbiddenWord word) => db.Set<ForbiddenWord>().Remove(word);

    public void ForgetTerms() => cache.Remove(CacheKey);
}

// Tabla PalabrasProhibidas, sembrada con el repertorio de ForbiddenWordCatalog.
internal sealed class ForbiddenWordConfiguration : IEntityTypeConfiguration<ForbiddenWord>
{
    public void Configure(EntityTypeBuilder<ForbiddenWord> builder)
    {
        builder.ToTable("PalabrasProhibidas");
        builder.HasKey(w => w.Term);
        builder.Property(w => w.Term).HasMaxLength(ForbiddenWord.MaxLength);
        builder.Property(w => w.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasData(ForbiddenWordCatalog.All);
    }
}
