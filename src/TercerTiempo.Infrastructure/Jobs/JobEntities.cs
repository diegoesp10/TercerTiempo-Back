using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TercerTiempo.Infrastructure.Jobs;

internal sealed class JobRun
{
    public Guid Id { get; set; }
    public string Job { get; set; } = "";
    public bool Manual { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Summary { get; set; }
    public string? Errors { get; set; }
    public int Requests { get; set; }

    // Datos de las ligas: CurrentRound, DateRange o FullSeason, y su descripción («Jornada en curso (21 oct – 28 oct)»).
    public string? ScopeKind { get; set; }
    public string? ScopeLabel { get; set; }
}

// Cada llamada real a una web externa (lo servido desde la caché no sale de aquí).
internal sealed class ScrapeRequest
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Provider { get; set; } = "";
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int? StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
    public Guid? JobRunId { get; set; }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> b)
    {
        b.ToTable("JobRuns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Job).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(1000);
        b.Property(x => x.ScopeKind).HasMaxLength(20);
        b.Property(x => x.ScopeLabel).HasMaxLength(200);
        b.HasIndex(x => new { x.Job, x.StartedAt });
    }
}

internal sealed class ScrapeRequestConfiguration : IEntityTypeConfiguration<ScrapeRequest>
{
    public void Configure(EntityTypeBuilder<ScrapeRequest> b)
    {
        b.ToTable("ScrapeRequests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.Method).HasMaxLength(10).IsRequired();
        b.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        b.Property(x => x.Error).HasMaxLength(500);
        b.HasIndex(x => x.At);
        b.HasIndex(x => new { x.Provider, x.At });
        b.HasIndex(x => x.JobRunId);
    }
}
