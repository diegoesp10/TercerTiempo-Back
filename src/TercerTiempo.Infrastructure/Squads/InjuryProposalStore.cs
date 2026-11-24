using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Squads;

internal sealed class InjuryProposalStore(AppDbContext db) : IInjuryProposalStore
{
    public Task<InjuryProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Set<InjuryProposal>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InjuryProposal>> GetBySquadAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        await db.Set<InjuryProposal>().Where(p => p.SquadId == squadId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InjuryProposal>> GetByMatchesAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        if (matchIds.Count == 0) return [];
        var proposals = new List<InjuryProposal>();
        foreach (var chunk in matchIds.Chunk(1000))
            proposals.AddRange(await db.Set<InjuryProposal>().Where(p => chunk.Contains(p.MatchId)).ToListAsync(cancellationToken));
        return proposals;
    }

    public async Task<IReadOnlyList<InjuryProposal>> GetExpiredAsync(DateTimeOffset detectedBefore, CancellationToken cancellationToken = default) =>
        await db.Set<InjuryProposal>()
            .Where(p => p.Status != InjuryProposalStatus.Registered && p.DetectedAt < detectedBefore)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InjuryProposal>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        await db.Set<InjuryProposal>().Where(p => p.Status == InjuryProposalStatus.Pending).ToListAsync(cancellationToken);

    public void Add(InjuryProposal proposal) => db.Set<InjuryProposal>().Add(proposal);

    public void Remove(InjuryProposal proposal) => db.Set<InjuryProposal>().Remove(proposal);
}

internal sealed class InjuryProposalConfiguration : IEntityTypeConfiguration<InjuryProposal>
{
    public void Configure(EntityTypeBuilder<InjuryProposal> builder)
    {
        builder.ToTable("InjuryProposals");
        builder.ConfigureAggregate();
        builder.Ignore(p => p.IsPending);

        builder.Property(p => p.Excerpt).HasMaxLength(MatchInjuryMention.MaxExcerptLength).IsRequired();
        builder.Property(p => p.Phrase).HasMaxLength(MatchInjuryMention.MaxPhraseLength).IsRequired();
        builder.Property(p => p.SourceUrl).HasMaxLength(MatchInjuryMention.MaxUrlLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(12);

        // El partido y la frase vienen de los datos reales (se copian y pueden cambiar): la propuesta guarda su copia y no
        // depende de ellos. Tampoco de la lesión registrada, que se puede borrar si se apuntó por error.
        builder.HasOne<Squad>().WithMany().HasForeignKey(p => p.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(p => p.SquadPlayerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Coach>().WithMany().HasForeignKey(p => p.DecidedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(p => p.DecidedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.SquadId, p.MatchId, p.SquadPlayerId }).IsUnique();
        builder.HasIndex(p => p.MatchId);
    }
}
