using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("Exercises");
        builder.ConfigureAggregate();

        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Material).HasMaxLength(300);

        builder.HasOne<Squad>().WithMany().HasForeignKey(e => e.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coach>().WithMany().HasForeignKey(e => e.CreatedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(e => e.CreatedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.SquadId);
    }
}

internal sealed class TrainingSessionConfiguration : IEntityTypeConfiguration<TrainingSession>
{
    public void Configure(EntityTypeBuilder<TrainingSession> builder)
    {
        builder.ToTable("TrainingSessions");
        builder.ConfigureAggregate();
        builder.Ignore(s => s.EndsAt);
        builder.Ignore(s => s.PlannedMinutes);

        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Title).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Location).HasMaxLength(150);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.HasOne<Squad>().WithMany().HasForeignKey(s => s.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coach>().WithMany().HasForeignKey(s => s.CreatedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(s => s.CreatedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasMany(s => s.Attendance).WithOne().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Attendance).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(s => new { s.SquadId, s.StartsAt });
        builder.HasIndex(s => s.SeriesId);
    }
}

internal sealed class TrainingItemConfiguration : IEntityTypeConfiguration<TrainingItem>
{
    public void Configure(EntityTypeBuilder<TrainingItem> builder)
    {
        builder.ToTable("TrainingItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Name).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Notes).HasMaxLength(500);

        // Borrar el ejercicio de la biblioteca deja la copia de la sesión sin enlace, pero entera: el enlace lo quita el
        // almacén antes de borrar (SQL Server no admite SET NULL aquí: la plantilla ya cascada a sesiones y ejercicios).
        builder.HasOne<Exercise>().WithMany().HasForeignKey(i => i.ExerciseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.SessionId, i.Order });
    }
}

internal sealed class TrainingAttendanceConfiguration : IEntityTypeConfiguration<TrainingAttendance>
{
    public void Configure(EntityTypeBuilder<TrainingAttendance> builder)
    {
        builder.ToTable("TrainingAttendances");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(200);

        // La ficha se queda aunque el jugador cause baja; si se borra la plantilla, cascada por la sesión.
        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(a => a.SquadPlayerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.SessionId, a.SquadPlayerId }).IsUnique();
    }
}

internal sealed class MatchCallUpConfiguration : IEntityTypeConfiguration<MatchCallUp>
{
    public void Configure(EntityTypeBuilder<MatchCallUp> builder)
    {
        builder.ToTable("MatchCallUps");
        builder.ConfigureAggregate();
        builder.Ignore(c => c.IsPublished);
        builder.Ignore(c => c.Called);

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.MeetPlace).HasMaxLength(150);
        builder.Property(c => c.Notes).HasMaxLength(1000);

        builder.HasOne<Squad>().WithMany().HasForeignKey(c => c.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Match>().WithMany().HasForeignKey(c => c.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Coach>().WithMany().HasForeignKey(c => c.CreatedByCoachId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Club>().WithMany().HasForeignKey(c => c.CreatedByClubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Entries).WithOne().HasForeignKey(e => e.CallUpId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Entries).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(c => new { c.SquadId, c.MatchId }).IsUnique();
    }
}

internal sealed class CallUpEntryConfiguration : IEntityTypeConfiguration<CallUpEntry>
{
    public void Configure(EntityTypeBuilder<CallUpEntry> builder)
    {
        builder.ToTable("CallUpEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Response).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ResponseNote).HasMaxLength(300);

        builder.HasOne<SquadPlayer>().WithMany().HasForeignKey(e => e.SquadPlayerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.CallUpId, e.SquadPlayerId }).IsUnique();
    }
}
