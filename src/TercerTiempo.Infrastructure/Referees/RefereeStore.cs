using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Referees;

internal sealed class RefereeStore(AppDbContext db) : IRefereeStore
{
    public Task<Referee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Set<Referee>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Referee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<Referee>().Where(r => list.Contains(r.Id)).ToListAsync(cancellationToken);
    }

    public Task<Referee?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default) =>
        db.Set<Referee>().FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken);

    public async Task<IReadOnlyList<Referee>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Set<Referee>().AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public void Add(Referee referee) => db.Set<Referee>().Add(referee);

    public async Task<IReadOnlyList<MatchOfficial>> GetOfficialsAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.Set<MatchOfficial>().Where(o => o.MatchId == matchId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchOfficial>> GetOfficialsByMatchesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        var list = matchIds.Distinct().ToList();
        return await db.Set<MatchOfficial>().AsNoTracking().Where(o => list.Contains(o.MatchId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchOfficial>> GetOfficialsByRefereeAsync(Guid refereeId, CancellationToken cancellationToken = default) =>
        await db.Set<MatchOfficial>().AsNoTracking().Where(o => o.RefereeId == refereeId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MatchOfficial>> GetAllOfficialsAsync(CancellationToken cancellationToken = default) =>
        await db.Set<MatchOfficial>().AsNoTracking().ToListAsync(cancellationToken);

    public void AddOfficial(MatchOfficial official) => db.Set<MatchOfficial>().Add(official);

    public async Task<IReadOnlyList<Guid>> GetMatchesWithOfficialsAsync(IEnumerable<Guid> teamIds, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var teams = teamIds.Distinct().ToList();
        return await db.Set<Match>()
            .Where(m => m.KickoffAt >= since && (teams.Contains(m.HomeTeamId) || teams.Contains(m.AwayTeamId)))
            .Where(m => db.Set<MatchOfficial>().Any(o => o.MatchId == m.Id))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OfficialsSource>> GetSheetsWithoutOfficialsAsync(string sheetProvider, CancellationToken cancellationToken = default) =>
        await db.Set<OfficialMatchSheet>().AsNoTracking()
            .Where(s => s.Provider == sheetProvider && s.Status == OfficialSheetStatus.Available)
            .Where(s => !db.Set<MatchOfficial>().Any(o => o.MatchId == s.MatchId))
            .Select(s => new OfficialsSource(s.MatchId, s.ExternalId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OfficialsSource>> GetMatchesToLookUpAsync(
        string provider, DateTimeOffset now, TimeSpan retryAfter, int maxAttempts, int max, CancellationToken cancellationToken = default)
    {
        var retryBefore = now - retryAfter;
        return await db.Set<Match>().AsNoTracking()
            .Where(m => m.Status == MatchStatus.Played && m.KickoffAt <= now && m.ExternalRef!.Provider == provider)
            .Where(m => !db.Set<MatchOfficial>().Any(o => o.MatchId == m.Id))
            .Where(m => !db.Set<MatchOfficialLookup>().Any(l => l.MatchId == m.Id && (l.Attempts >= maxAttempts || l.CheckedAt > retryBefore)))
            .OrderByDescending(m => m.KickoffAt)
            .Take(max)
            .Select(m => new OfficialsSource(m.Id, m.ExternalRef!.ExternalId))
            .ToListAsync(cancellationToken);
    }

    public Task<MatchOfficialLookup?> GetLookupAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        db.Set<MatchOfficialLookup>().FirstOrDefaultAsync(l => l.MatchId == matchId, cancellationToken);

    public void AddLookup(MatchOfficialLookup lookup) => db.Set<MatchOfficialLookup>().Add(lookup);

    public async Task<IReadOnlyList<RefereeRating>> GetRatingsByMatchesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        var list = matchIds.Distinct().ToList();
        return await db.Set<RefereeRating>().Where(r => list.Contains(r.MatchId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RefereeRating>> GetRatingsByRefereeAsync(Guid refereeId, CancellationToken cancellationToken = default) =>
        await db.Set<RefereeRating>().AsNoTracking().Where(r => r.RefereeId == refereeId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefereeRating>> GetAllRatingsAsync(CancellationToken cancellationToken = default) =>
        await db.Set<RefereeRating>().AsNoTracking().ToListAsync(cancellationToken);

    public void AddRating(RefereeRating rating) => db.Set<RefereeRating>().Add(rating);
}

internal sealed class RefereeConfiguration : IEntityTypeConfiguration<Referee>
{
    public void Configure(EntityTypeBuilder<Referee> builder)
    {
        builder.ToTable("Referees");
        builder.ConfigureAggregate();
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Slug).HasMaxLength(150);
        builder.HasIndex(r => r.NormalizedName).IsUnique();
        builder.HasIndex(r => r.Slug);
    }
}

internal sealed class MatchOfficialConfiguration : IEntityTypeConfiguration<MatchOfficial>
{
    public void Configure(EntityTypeBuilder<MatchOfficial> builder)
    {
        builder.ToTable("MatchOfficials");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Role).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.Source).HasMaxLength(40).IsRequired();
        builder.HasOne<Match>().WithMany().HasForeignKey(o => o.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Referee>().WithMany().HasForeignKey(o => o.RefereeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.MatchId, o.Role }).IsUnique();
        builder.HasIndex(o => o.RefereeId);
    }
}

internal sealed class MatchOfficialLookupConfiguration : IEntityTypeConfiguration<MatchOfficialLookup>
{
    public void Configure(EntityTypeBuilder<MatchOfficialLookup> builder)
    {
        builder.ToTable("MatchOfficialLookups");
        builder.HasKey(l => l.MatchId);
        builder.HasOne<Match>().WithMany().HasForeignKey(l => l.MatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefereeRatingConfiguration : IEntityTypeConfiguration<RefereeRating>
{
    public void Configure(EntityTypeBuilder<RefereeRating> builder)
    {
        builder.ToTable("RefereeRatings");
        builder.ConfigureAggregate();
        builder.Property(r => r.Role).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Comment).HasMaxLength(RefereeRating.MaxCommentLength);
        builder.HasOne<Match>().WithMany().HasForeignKey(r => r.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Referee>().WithMany().HasForeignKey(r => r.RefereeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(r => r.RatedByClubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coach>().WithMany().HasForeignKey(r => r.RatedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.MatchId, r.TeamId, r.RefereeId }).IsUnique();
        builder.HasIndex(r => r.RefereeId);
    }
}
