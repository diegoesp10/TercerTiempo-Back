using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Performances;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Identity;

namespace TercerTiempo.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Coach> Coaches => Set<Coach>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<ClubTeam> ClubTeams => Set<ClubTeam>();
    public DbSet<CoachAppointment> CoachAppointments => Set<CoachAppointment>();
    public DbSet<Squad> Squads => Set<Squad>();
    public DbSet<SquadStaff> SquadStaff => Set<SquadStaff>();
    public DbSet<SquadPlayer> SquadPlayers => Set<SquadPlayer>();
    public DbSet<PlayerInjury> PlayerInjuries => Set<PlayerInjury>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Standing> Standings => Set<Standing>();
    public DbSet<PlayerMatchPerformance> Performances => Set<PlayerMatchPerformance>();
    public DbSet<PerformanceSuggestion> PerformanceSuggestions => Set<PerformanceSuggestion>();
    public DbSet<TeamMatchStats> TeamMatchStats => Set<TeamMatchStats>();
    public DbSet<OfficialMatchSheet> OfficialSheets => Set<OfficialMatchSheet>();
    public DbSet<OfficialSheetLine> OfficialSheetLines => Set<OfficialSheetLine>();
    public DbSet<OfficialSheetEvent> OfficialSheetEvents => Set<OfficialSheetEvent>();
    public DbSet<MatchVideo> MatchVideos => Set<MatchVideo>();
    public DbSet<TeamLicense> TeamLicenses => Set<TeamLicense>();
    public DbSet<LicensePayment> LicensePayments => Set<LicensePayment>();
    public DbSet<VideoPurchase> VideoPurchases => Set<VideoPurchase>();
    public DbSet<VideoUploadSession> VideoUploads => Set<VideoUploadSession>();
    public DbSet<VideoShareLink> VideoShareLinks => Set<VideoShareLink>();
    public DbSet<ErrorMessage> ErrorMessages => Set<ErrorMessage>();

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

        int result;
        try
        {
            result = await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is SquadPlayer))
        {
            throw new ConflictException(ErrorCode.PlayerInviteChanged);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }
            && ex.Entries.Any(e => e.Entity is SquadPlayer { PlayerId: not null }))
        {
            throw new ConflictException(ErrorCode.PlayerAlreadyInSquad);
        }

        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();

        return result;
    }
}
