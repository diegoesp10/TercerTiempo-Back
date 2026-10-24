using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class MatchVideoConfiguration : IEntityTypeConfiguration<MatchVideo>
{
    public void Configure(EntityTypeBuilder<MatchVideo> builder)
    {
        builder.ToTable("MatchVideos");
        builder.ConfigureAggregate();
        builder.Ignore(v => v.IsDeleted);
        builder.Ignore(v => v.IsFree);

        builder.Property(v => v.Part).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(v => v.IsPublic);
        builder.Property(v => v.Title).HasMaxLength(150).IsRequired();
        builder.Property(v => v.FileName).HasMaxLength(255).IsRequired();
        builder.Property(v => v.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(v => v.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(v => v.Currency).HasMaxLength(3).IsRequired();

        builder.HasOne<Squad>().WithMany().HasForeignKey(v => v.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(v => v.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Match>().WithMany().HasForeignKey(v => v.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Season>().WithMany().HasForeignKey(v => v.SeasonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coach>().WithMany().HasForeignKey(v => v.UploadedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(v => v.UploadedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => v.MatchId);
        builder.HasIndex(v => v.SquadId);
    }
}

internal sealed class VideoPurchaseConfiguration : IEntityTypeConfiguration<VideoPurchase>
{
    public void Configure(EntityTypeBuilder<VideoPurchase> builder)
    {
        builder.ToTable("VideoPurchases");
        builder.ConfigureAggregate();

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.PaymentReference).HasMaxLength(200).IsRequired();

        builder.HasOne<MatchVideo>().WithMany().HasForeignKey(p => p.VideoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coach>().WithMany().HasForeignKey(p => p.CoachId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.VideoId, p.CoachId }).IsUnique();
        builder.HasIndex(p => p.CoachId);
    }
}

internal sealed class VideoUploadSessionConfiguration : IEntityTypeConfiguration<VideoUploadSession>
{
    public void Configure(EntityTypeBuilder<VideoUploadSession> builder)
    {
        builder.ToTable("VideoUploads");
        builder.ConfigureAggregate();
        builder.Ignore(s => s.IsComplete);
        builder.Ignore(s => s.NextChunk);
        builder.Ignore(s => s.TotalChunks);

        builder.Property(s => s.Part).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Title).HasMaxLength(150);
        builder.Property(s => s.FileName).HasMaxLength(255).IsRequired();
        builder.Property(s => s.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(s => s.StorageKey).HasMaxLength(500).IsRequired();

        builder.HasOne<Squad>().WithMany().HasForeignKey(s => s.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Match>().WithMany().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coach>().WithMany().HasForeignKey(s => s.CoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(s => s.ClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.CoachId);
    }
}

internal sealed class VideoShareLinkConfiguration : IEntityTypeConfiguration<VideoShareLink>
{
    public void Configure(EntityTypeBuilder<VideoShareLink> builder)
    {
        builder.ToTable("VideoShareLinks");
        builder.ConfigureAggregate();

        builder.Property(l => l.Token).HasMaxLength(100).IsRequired();

        builder.HasOne<MatchVideo>().WithMany().HasForeignKey(l => l.VideoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coach>().WithMany().HasForeignKey(l => l.CreatedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(l => l.CreatedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.Token).IsUnique();
        builder.HasIndex(l => l.VideoId);
    }
}
