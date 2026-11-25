using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Sanctions;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Sanctions;

internal sealed class CommitteeSanctionStore(AppDbContext db) : ICommitteeSanctionStore
{
    public async Task<IReadOnlyList<CommitteeResolution>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Set<CommitteeResolution>().AsNoTracking()
            .Where(r => r.SeasonId == seasonId)
            .OrderBy(r => r.MeetingOn).ThenBy(r => r.Matchday)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CommitteeResolution>> GetStoredBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await db.Set<CommitteeResolution>().Where(r => r.SeasonId == seasonId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetSeasonsWithMeetingsSinceAsync(DateOnly since, CancellationToken cancellationToken = default) =>
        await db.Set<CommitteeResolution>().AsNoTracking()
            .Where(r => r.MeetingOn >= since)
            .Select(r => r.SeasonId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CommitteeSanctionCheck>> GetChecksAsync(CancellationToken cancellationToken = default) =>
        await db.Set<CommitteeSanctionCheck>().ToListAsync(cancellationToken);

    public void Add(CommitteeResolution resolution) => db.Set<CommitteeResolution>().Add(resolution);

    public void Remove(CommitteeResolution resolution) => db.Set<CommitteeResolution>().Remove(resolution);

    public void AddCheck(CommitteeSanctionCheck check) => db.Set<CommitteeSanctionCheck>().Add(check);
}

internal sealed class CommitteeResolutionConfiguration : IEntityTypeConfiguration<CommitteeResolution>
{
    public void Configure(EntityTypeBuilder<CommitteeResolution> builder)
    {
        builder.ToTable("CommitteeResolutions");
        builder.ConfigureAggregate();

        builder.Property(r => r.Provider).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Committee).HasMaxLength(CommitteeResolution.MaxCommitteeLength).IsRequired();
        builder.Property(r => r.Fingerprint).HasMaxLength(64).IsRequired();

        builder.HasOne<Season>().WithMany().HasForeignKey(r => r.SeasonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.SeasonId, r.Committee, r.MeetingOn, r.Matchday }).IsUnique();
        builder.HasIndex(r => r.MeetingOn);

        builder.HasMany(r => r.Sanctions).WithOne().HasForeignKey(s => s.ResolutionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Sanctions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class CommitteeSanctionConfiguration : IEntityTypeConfiguration<CommitteeSanction>
{
    public void Configure(EntityTypeBuilder<CommitteeSanction> builder)
    {
        builder.ToTable("CommitteeSanctions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TeamName).HasMaxLength(CommitteeSanction.MaxNameLength).IsRequired();
        builder.Property(s => s.PersonName).HasMaxLength(CommitteeSanction.MaxNameLength).IsRequired();
        builder.Property(s => s.NormalizedName).HasMaxLength(CommitteeSanction.MaxNameLength).IsRequired();
        builder.Property(s => s.Article).HasMaxLength(CommitteeSanction.MaxArticleLength).IsRequired();
        builder.Property(s => s.Reason).HasMaxLength(CommitteeSanction.MaxReasonLength).IsRequired();
        // El 0 no es ningún tipo de sanción: solo sin valor se queda el de la base (HasSentinel, para que EF no avise).
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(10).HasDefaultValue(CommitteeSanctionKind.Player)
            .HasSentinel((CommitteeSanctionKind)0);

        builder.HasIndex(s => s.ResolutionId);
    }
}

internal sealed class CommitteeSanctionCheckConfiguration : IEntityTypeConfiguration<CommitteeSanctionCheck>
{
    public void Configure(EntityTypeBuilder<CommitteeSanctionCheck> builder)
    {
        builder.ToTable("CommitteeSanctionChecks");
        builder.HasKey(c => c.SeasonId);
        builder.HasOne<Season>().WithMany().HasForeignKey(c => c.SeasonId).OnDelete(DeleteBehavior.Cascade);
    }
}
