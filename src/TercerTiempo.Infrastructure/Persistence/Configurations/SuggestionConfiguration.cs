using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class SuggestionConfiguration : IEntityTypeConfiguration<PerformanceSuggestion>
{
    public void Configure(EntityTypeBuilder<PerformanceSuggestion> builder)
    {
        builder.ToTable("PerformanceSuggestions");
        builder.ConfigureAggregate();
        builder.Ignore(s => s.IsPending);

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Message).HasMaxLength(PerformanceSuggestion.MaxMessageLength);
        builder.Property(s => s.ResolutionNote).HasMaxLength(PerformanceSuggestion.MaxMessageLength);

        builder.HasOne<Squad>().WithMany().HasForeignKey(s => s.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(s => s.SquadPlayerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Player>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Match>().WithMany().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Coach>().WithMany().HasForeignKey(s => s.ResolvedByCoachId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Club>().WithMany().HasForeignKey(s => s.ResolvedByClubId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => new { s.SquadId, s.Status });
        builder.HasIndex(s => s.PlayerId);
    }
}
