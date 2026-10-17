using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Infrastructure.Persistence.Configurations;

internal sealed class SquadConfiguration : IEntityTypeConfiguration<Squad>
{
    public void Configure(EntityTypeBuilder<Squad> builder)
    {
        builder.ToTable("Squads");
        builder.ConfigureAggregate();

        builder.Property(s => s.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.InviteCode).HasMaxLength(20).IsRequired();

        builder.HasOne<Team>().WithMany().HasForeignKey(s => s.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Season>().WithMany().HasForeignKey(s => s.SeasonId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TeamId, s.SeasonId }).IsUnique();
        builder.HasIndex(s => s.InviteCode).IsUnique();

        builder.HasMany(s => s.Staff).WithOne().HasForeignKey(m => m.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Staff).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(s => s.Players).WithOne().HasForeignKey(p => p.SquadId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Players).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.Ignore(s => s.ActiveStaff);
        builder.Ignore(s => s.ActivePlayers);
    }
}

internal sealed class SquadStaffConfiguration : IEntityTypeConfiguration<SquadStaff>
{
    public void Configure(EntityTypeBuilder<SquadStaff> builder)
    {
        builder.ToTable("SquadStaff");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(s => s.IsActive);

        builder.HasOne<Coach>().WithMany().HasForeignKey(s => s.CoachId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.SquadId, s.CoachId });
        builder.HasIndex(s => s.CoachId);
    }
}

internal sealed class SquadPlayerConfiguration : IEntityTypeConfiguration<SquadPlayer>
{
    public void Configure(EntityTypeBuilder<SquadPlayer> builder)
    {
        builder.ToTable("SquadPlayers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.FullName).HasMaxLength(150).IsRequired();
        builder.Property(p => p.NormalizedName).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Position).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(p => p.IsActive);
        builder.Ignore(p => p.IsLinked);

        builder.HasOne<Player>().WithMany().HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<RosterEntry>().WithMany().HasForeignKey(p => p.RosterEntryId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.SquadId, p.NormalizedName });
        builder.HasIndex(p => p.PlayerId);
    }
}
