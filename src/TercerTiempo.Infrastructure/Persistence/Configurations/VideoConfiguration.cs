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

        builder.Property(v => v.Part).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Title).HasMaxLength(150).IsRequired();
        builder.Property(v => v.FileName).HasMaxLength(255).IsRequired();
        builder.Property(v => v.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(v => v.StorageKey).HasMaxLength(500).IsRequired();

        // Compresión en segundo plano. Los valores por defecto en SQL dejan insertar a una API anterior a estas columnas.
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(VideoStatus.Uploaded);
        builder.Property(v => v.ProcessingProgress).HasDefaultValue(0);
        builder.Property(v => v.ProcessingAttempts).HasDefaultValue(0);
        builder.Property(v => v.ProcessingError).HasMaxLength(MatchVideo.MaxErrorLength);
        // El compresor y las peticiones de borrado pueden tocar la misma fila: quien llega tarde se entera.
        builder.Property<byte[]>("Version").IsRowVersion();
        builder.HasIndex(v => v.Status);

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

internal sealed class MatchVideoAnalysisConfiguration : IEntityTypeConfiguration<MatchVideoAnalysis>
{
    public void Configure(EntityTypeBuilder<MatchVideoAnalysis> builder)
    {
        builder.ToTable("MatchVideoAnalyses");
        builder.ConfigureAggregate();

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Error).HasMaxLength(MatchVideoAnalysis.MaxErrorLength);
        builder.Property(a => a.OutputPath).HasMaxLength(500);
        builder.Property(a => a.AnalyzerVersion).HasMaxLength(40);
        builder.Property(a => a.Weights).HasMaxLength(500);
        builder.Property(a => a.BallFramesPct).HasPrecision(5, 1);
        builder.Property(a => a.CalibratedFramesPct).HasPrecision(5, 1);
        builder.Property(a => a.PitchLengthM).HasPrecision(5, 1);
        builder.Property(a => a.PitchWidthM).HasPrecision(5, 1);
        // El analizador (servicio de vídeo) y las peticiones de la API (volver a analizar, volcar) pueden tocar la misma
        // fila: quien llega tarde se entera.
        builder.Property<byte[]>("Version").IsRowVersion();

        // Un análisis por vídeo; se va con él. La plantilla y el partido van sin clave ajena (ya los ata el vídeo).
        builder.HasOne<MatchVideo>().WithMany().HasForeignKey(a => a.VideoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.VideoId).IsUnique();
        builder.HasIndex(a => a.Status);
    }
}

internal sealed class VideoAnalysisCropConfiguration : IEntityTypeConfiguration<VideoAnalysisCrop>
{
    public void Configure(EntityTypeBuilder<VideoAnalysisCrop> builder)
    {
        builder.ToTable("MatchVideoAnalysisCrops");
        builder.HasKey(c => new { c.AnalysisId, c.Tracklet });
        builder.Property(c => c.Content).IsRequired();

        builder.HasOne<MatchVideoAnalysis>().WithMany().HasForeignKey(c => c.AnalysisId).OnDelete(DeleteBehavior.Cascade);
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
