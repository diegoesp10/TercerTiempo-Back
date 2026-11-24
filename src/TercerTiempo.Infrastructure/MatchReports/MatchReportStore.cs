using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.MatchReports;

internal sealed class MatchReportStore(AppDbContext db) : IMatchReportStore
{
    public async Task<IReadOnlyList<MatchInjuryMention>> GetStoredByMatchesAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        if (matchIds.Count == 0) return [];
        return await db.Set<MatchInjuryMention>().Where(m => matchIds.Contains(m.MatchId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchInjuryMention>> GetUpdatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        await db.Set<MatchInjuryMention>().AsNoTracking()
            .Where(m => m.UpdatedAt >= since)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchReportSource>> GetCandidatesAsync(string provider, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await db.Set<Match>().AsNoTracking()
            .Where(m => m.Status == MatchStatus.Played && m.KickoffAt >= from && m.KickoffAt <= to && m.ExternalRef!.Provider == provider)
            .Where(m => db.Set<OfficialMatchSheet>().Any(s => s.MatchId == m.Id && s.Status == OfficialSheetStatus.Available))
            .Where(m => !db.Set<MatchReportCheck>().Any(c => c.MatchId == m.Id && c.Done))
            .Select(m => new MatchReportSource(m.Id, m.ExternalRef!.ExternalId, m.KickoffAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchReportCheck>> GetChecksAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        if (matchIds.Count == 0) return [];
        // De a mil: una lista muy larga de parámetros no la admite SQL Server.
        var checks = new List<MatchReportCheck>();
        foreach (var chunk in matchIds.Chunk(1000))
            checks.AddRange(await db.Set<MatchReportCheck>().Where(c => chunk.Contains(c.MatchId)).ToListAsync(cancellationToken));
        return checks;
    }

    public Task<int> RemoveMentionsUpdatedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        db.Set<MatchInjuryMention>().Where(m => m.UpdatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);

    public void Add(MatchInjuryMention mention) => db.Set<MatchInjuryMention>().Add(mention);

    public void Remove(MatchInjuryMention mention) => db.Set<MatchInjuryMention>().Remove(mention);

    public void AddCheck(MatchReportCheck check) => db.Set<MatchReportCheck>().Add(check);
}

internal sealed class MatchInjuryMentionConfiguration : IEntityTypeConfiguration<MatchInjuryMention>
{
    public void Configure(EntityTypeBuilder<MatchInjuryMention> builder)
    {
        builder.ToTable("MatchInjuryMentions");
        builder.ConfigureAggregate();

        builder.Property(m => m.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.PlayerName).HasMaxLength(MatchInjuryMention.MaxNameLength).IsRequired();
        builder.Property(m => m.NormalizedName).HasMaxLength(MatchInjuryMention.MaxNameLength).IsRequired();
        builder.Property(m => m.Excerpt).HasMaxLength(MatchInjuryMention.MaxExcerptLength).IsRequired();
        builder.Property(m => m.Phrase).HasMaxLength(MatchInjuryMention.MaxPhraseLength).IsRequired();
        builder.Property(m => m.Provider).HasMaxLength(50).IsRequired();
        builder.Property(m => m.SourceUrl).HasMaxLength(MatchInjuryMention.MaxUrlLength).IsRequired();

        builder.HasOne<Match>().WithMany().HasForeignKey(m => m.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.MatchId, m.Side, m.NormalizedName }).IsUnique();
        builder.HasIndex(m => m.UpdatedAt);
    }
}

internal sealed class MatchReportCheckConfiguration : IEntityTypeConfiguration<MatchReportCheck>
{
    public void Configure(EntityTypeBuilder<MatchReportCheck> builder)
    {
        builder.ToTable("MatchReportChecks");
        builder.HasKey(c => c.MatchId);
        builder.HasOne<Match>().WithMany().HasForeignKey(c => c.MatchId).OnDelete(DeleteBehavior.Cascade);
    }
}
