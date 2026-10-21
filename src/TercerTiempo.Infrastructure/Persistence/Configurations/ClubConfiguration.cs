using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.ToTable("Clubs");
        builder.ConfigureAggregate();

        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.NormalizedName).HasMaxLength(120).IsRequired();

        builder.HasIndex(c => c.UserId).IsUnique();

        builder.HasMany(c => c.Teams).WithOne().HasForeignKey(t => t.ClubId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Teams).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class ClubTeamConfiguration : IEntityTypeConfiguration<ClubTeam>
{
    public void Configure(EntityTypeBuilder<ClubTeam> builder)
    {
        builder.ToTable("ClubTeams");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.HasOne<Team>().WithMany().HasForeignKey(t => t.TeamId).OnDelete(DeleteBehavior.Cascade);

        // Un equipo solo puede pertenecer a un club.
        builder.HasIndex(t => t.TeamId).IsUnique();
    }
}

internal sealed class CoachAppointmentConfiguration : IEntityTypeConfiguration<CoachAppointment>
{
    public void Configure(EntityTypeBuilder<CoachAppointment> builder)
    {
        builder.ToTable("CoachAppointments");
        builder.ConfigureAggregate();
        builder.Ignore(a => a.IsRedeemed);
        builder.Ignore(a => a.IsRevoked);

        builder.Property(a => a.CoachName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.NormalizedCoachName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(256);
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.AccessCode).HasMaxLength(20).IsRequired();

        builder.HasOne<Club>().WithMany().HasForeignKey(a => a.ClubId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Team>().WithMany().HasForeignKey(a => a.TeamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Season>().WithMany().HasForeignKey(a => a.SeasonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Coach>().WithMany().HasForeignKey(a => a.CoachId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => a.AccessCode).IsUnique();
        builder.HasIndex(a => a.ClubId);
        builder.HasIndex(a => a.CoachId);
        builder.HasIndex(a => new { a.TeamId, a.SeasonId });
    }
}
