using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class InjuryDocumentConfiguration : IEntityTypeConfiguration<InjuryDocument>
{
    public void Configure(EntityTypeBuilder<InjuryDocument> builder)
    {
        builder.ToTable("InjuryDocuments");
        builder.ConfigureAggregate();

        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.FileName).HasMaxLength(InjuryDocument.FileNameMaxLength).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(d => d.StorageKey).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Note).HasMaxLength(InjuryDocument.NoteMaxLength);

        // La plantilla ya borra sus lesiones en cascada; de ahí bajan a sus documentos (un segundo camino desde la
        // plantilla no lo admite SQL Server).
        builder.HasOne<PlayerInjury>().WithMany().HasForeignKey(d => d.InjuryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Squad>().WithMany().HasForeignKey(d => d.SquadId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Coach>().WithMany().HasForeignKey(d => d.UploadedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(d => d.UploadedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.InjuryId);
    }
}
