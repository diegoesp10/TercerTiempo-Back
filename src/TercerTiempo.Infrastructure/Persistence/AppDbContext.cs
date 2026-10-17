using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Identity;

namespace TercerTiempo.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<SquadMembership> SquadMemberships => Set<SquadMembership>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Standing> Standings => Set<Standing>();
    public DbSet<PlayerMatchPerformance> Performances => Set<PlayerMatchPerformance>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Punto de extensión: aquí se despacharían los eventos de dominio (outbox, handlers in-process...).
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();

        return result;
    }
}
