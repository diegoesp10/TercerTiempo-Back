using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class OfficialSheetConfiguration : IEntityTypeConfiguration<OfficialMatchSheet>
{
    public void Configure(EntityTypeBuilder<OfficialMatchSheet> builder)
    {
        builder.ToTable("OfficialSheets");
        builder.ConfigureAggregate();
        builder.Ignore(s => s.IsAvailable);

        builder.Property(s => s.Provider).HasMaxLength(50).IsRequired();
        builder.Property(s => s.ExternalId).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.SourceUrl).HasMaxLength(500);
        builder.Property(s => s.Referee).HasMaxLength(150);
        builder.Property(s => s.HomeTeamName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.AwayTeamName).HasMaxLength(150).IsRequired();

        builder.HasOne<Match>().WithMany().HasForeignKey(s => s.MatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.MatchId).IsUnique();

        builder.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SheetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(s => s.Events).WithOne().HasForeignKey(e => e.SheetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Events).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class OfficialSheetLineConfiguration : IEntityTypeConfiguration<OfficialSheetLine>
{
    public void Configure(EntityTypeBuilder<OfficialSheetLine> builder)
    {
        builder.ToTable("OfficialSheetLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Ignore(l => l.Played);

        builder.Property(l => l.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(l => l.FullName).HasMaxLength(150).IsRequired();
        builder.Property(l => l.NormalizedName).HasMaxLength(150).IsRequired();

        builder.HasIndex(l => l.SheetId);
    }
}

internal sealed class OfficialSheetEventConfiguration : IEntityTypeConfiguration<OfficialSheetEvent>
{
    public void Configure(EntityTypeBuilder<OfficialSheetEvent> builder)
    {
        builder.ToTable("OfficialSheetEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PlayerName).HasMaxLength(150).IsRequired();
        builder.Property(e => e.RelatedPlayerName).HasMaxLength(150);

        builder.HasIndex(e => e.SheetId);
    }
}
