using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Application.Privacy;
using TercerTiempo.Domain.Privacy;
using TercerTiempo.Infrastructure.Persistence;
using TercerTiempo.Infrastructure.Persistence.Configurations;

namespace TercerTiempo.Infrastructure.Privacy;

internal sealed class CookieStore(AppDbContext db) : ICookieStore
{
    public async Task<IReadOnlyList<CookieDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.CookieDefinitions.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CookieDefinition>> GetAllForUpdateAsync(CancellationToken cancellationToken = default) =>
        await db.CookieDefinitions.ToListAsync(cancellationToken);

    public void Add(CookieDefinition cookie) => db.CookieDefinitions.Add(cookie);

    public void Remove(CookieDefinition cookie) => db.CookieDefinitions.Remove(cookie);

    public Task<CookiePolicy?> GetPolicyAsync(CancellationToken cancellationToken = default) =>
        db.CookiePolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    public Task<CookiePolicy?> GetPolicyForUpdateAsync(CancellationToken cancellationToken = default) =>
        db.CookiePolicies.FirstOrDefaultAsync(cancellationToken);

    public void AddPolicy(CookiePolicy policy) => db.CookiePolicies.Add(policy);

    public void AddConsent(CookieConsent consent) => db.CookieConsents.Add(consent);

    public Task<CookieConsent?> GetLatestConsentAsync(string visitorId, CancellationToken cancellationToken = default) =>
        db.CookieConsents.AsNoTracking()
            .Where(c => c.VisitorId == visitorId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CookieConsent>> GetConsentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await db.CookieConsents.AsNoTracking()
            .Where(c => c.CreatedAt >= from && c.CreatedAt < to)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
}

internal sealed class AnalyticsStore(AppDbContext db) : IAnalyticsStore
{
    public void AddRange(IEnumerable<AnalyticsEvent> events) => db.AnalyticsEvents.AddRange(events);

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        db.AnalyticsEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}

// Los recuentos se hacen en SQL Server: los eventos pueden ser muchos y aquí solo llegan los totales.
internal sealed class AnalyticsReports(AppDbContext db) : IAnalyticsReports
{
    public async Task<AnalyticsRaw> ReportAsync(AnalyticsFilter filter, int top, CancellationToken cancellationToken)
    {
        var events = db.AnalyticsEvents.AsNoTracking().Where(e => e.OccurredAt >= filter.From && e.OccurredAt < filter.To);
        if (filter.ActorClubId is { } actorClub) events = events.Where(e => e.ActorClubId == actorClub);
        if (filter.SubjectClubId is { } subjectClub) events = events.Where(e => e.SubjectClubId == subjectClub);

        var views = events.Where(e => e.Type == AnalyticsEventType.PageView);
        var totals = new AnalyticsTotalsDto(
            await views.CountAsync(cancellationToken),
            await events.Select(e => e.VisitorId).Distinct().CountAsync(cancellationToken),
            await events.Select(e => e.SessionId).Distinct().CountAsync(cancellationToken),
            await events.CountAsync(e => e.Type == AnalyticsEventType.Search, cancellationToken),
            await events.CountAsync(e => e.Type == AnalyticsEventType.Click, cancellationToken));

        var from = filter.From;
        var days = (await views
                .GroupBy(e => EF.Functions.DateDiffDay(from, e.OccurredAt))
                .Select(g => new { Offset = g.Key, Views = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
                .ToListAsync(cancellationToken))
            .Select(d => new AnalyticsDayDto(DateOnly.FromDateTime(from.UtcDateTime).AddDays(d.Offset), d.Views, d.Visitors))
            .OrderBy(d => d.Day)
            .ToList();

        var pages = (await views
                .GroupBy(e => e.RouteName ?? e.Path)
                .Select(g => new { Key = g.Key, Example = g.Min(e => e.Path), Count = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync(cancellationToken))
            .Select(x => new AnalyticsCountDto(x.Key, x.Example, x.Count, x.Visitors))
            .ToList();

        var entities = (await views
                .Where(e => e.EntityType != null && e.EntityId != null)
                .GroupBy(e => new { e.EntityType, e.EntityId })
                .Select(g => new { g.Key.EntityType, g.Key.EntityId, Count = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync(cancellationToken))
            .Select(x => (x.EntityType!.Value, x.EntityId!.Value, x.Count, x.Visitors))
            .ToList();

        var actors = (await events
                .GroupBy(e => e.Actor)
                .Select(g => new { Actor = g.Key, Count = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
                .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.Count)
            .Select(x => new AnalyticsCountDto(x.Actor.ToString(), x.Actor.ToString(), x.Count, x.Visitors))
            .ToList();

        var consents = db.CookieConsents.AsNoTracking().Where(c => c.CreatedAt >= filter.From && c.CreatedAt < filter.To);
        var consentStats = new ConsentStatsDto(
            await consents.CountAsync(cancellationToken),
            await consents.CountAsync(c => c.Action == ConsentAction.AcceptAll, cancellationToken),
            await consents.CountAsync(c => c.Action == ConsentAction.RejectAll, cancellationToken),
            await consents.CountAsync(c => c.Action == ConsentAction.Custom, cancellationToken),
            await consents.CountAsync(c => c.Analytics, cancellationToken));

        return new AnalyticsRaw(
            totals, days, pages,
            await LabelsAsync(events.Where(e => e.Type == AnalyticsEventType.Search), top, cancellationToken),
            await LabelsAsync(events.Where(e => e.Type == AnalyticsEventType.Click), top, cancellationToken),
            entities, actors,
            await ActorClubsAsync(events, top, cancellationToken),
            await SubjectClubsAsync(views, top, cancellationToken),
            consentStats);
    }

    private static async Task<IReadOnlyList<AnalyticsCountDto>> LabelsAsync(IQueryable<AnalyticsEvent> events, int top, CancellationToken cancellationToken) =>
        (await events
            .Where(e => e.Label != null)
            .GroupBy(e => e.Label!)
            .Select(g => new { Label = g.Key, Count = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(cancellationToken))
        .Select(x => new AnalyticsCountDto(x.Label, x.Label, x.Count, x.Visitors))
        .ToList();

    private static async Task<IReadOnlyList<(Guid, int, int)>> ActorClubsAsync(IQueryable<AnalyticsEvent> events, int top, CancellationToken cancellationToken) =>
        (await events
            .Where(e => e.ActorClubId != null)
            .GroupBy(e => e.ActorClubId!.Value)
            .Select(g => new { ClubId = g.Key, Count = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(cancellationToken))
        .Select(x => (x.ClubId, x.Count, x.Visitors))
        .ToList();

    private static async Task<IReadOnlyList<(Guid, int, int)>> SubjectClubsAsync(IQueryable<AnalyticsEvent> events, int top, CancellationToken cancellationToken) =>
        (await events
            .Where(e => e.SubjectClubId != null)
            .GroupBy(e => e.SubjectClubId!.Value)
            .Select(g => new { ClubId = g.Key, Count = g.Count(), Visitors = g.Select(e => e.VisitorId).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(cancellationToken))
        .Select(x => (x.ClubId, x.Count, x.Visitors))
        .ToList();
}

internal sealed class CookieDefinitionConfiguration : IEntityTypeConfiguration<CookieDefinition>
{
    public void Configure(EntityTypeBuilder<CookieDefinition> builder)
    {
        builder.ToTable("CookieDefinitions");
        builder.ConfigureAggregate();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Storage).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Provider).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Purpose).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Duration).HasMaxLength(100).IsRequired();
        builder.Ignore(c => c.RequiresConsent);

        // Lo que la web guarda hoy en el navegador (no usa cookies propiamente dichas: todo es almacenamiento local).
        const string us = "Tercer Tiempo";
        builder.HasData(
            Seed(1, "tt.token", CookieCategory.Necessary, CookieStorage.LocalStorage, us, "Mantiene la sesión iniciada (token de acceso).", "Hasta cerrar sesión", 10),
            Seed(2, "tt.account", CookieCategory.Necessary, CookieStorage.LocalStorage, us, "Datos de tu cuenta para mostrar la web sin esperar a la API.", "Hasta cerrar sesión", 20),
            Seed(3, "tt.consent", CookieCategory.Necessary, CookieStorage.LocalStorage, us, "Guarda tu elección sobre las cookies para no volver a preguntarte.", "13 meses", 30),
            Seed(4, "tt.admin-session", CookieCategory.Necessary, CookieStorage.SessionStorage, us, "Sesión del panel de administración.", "Hasta cerrar la pestaña", 40),
            Seed(5, "tt.theme", CookieCategory.Preferences, CookieStorage.LocalStorage, us, "Tema claro u oscuro que eliges.", "Permanente, hasta que la borres", 10),
            Seed(6, "tt.season", CookieCategory.Preferences, CookieStorage.LocalStorage, us, "Temporada que estás consultando.", "Permanente, hasta que la borres", 20),
            Seed(7, "tt.my-club-first", CookieCategory.Preferences, CookieStorage.LocalStorage, us, "Si tu club sale primero en los listados.", "Permanente, hasta que la borres", 30),
            Seed(8, "tt.home-competition", CookieCategory.Preferences, CookieStorage.LocalStorage, us, "Última liga que has consultado en la portada.", "Permanente, hasta que la borres", 40),
            Seed(9, "tt.aid", CookieCategory.Analytics, CookieStorage.LocalStorage, us, "Identificador anónimo de tu navegador para contar visitantes, páginas vistas, búsquedas y clics. Los datos se quedan en Tercer Tiempo: no se envían a terceros.", "13 meses", 10),
            Seed(10, "tt.sid", CookieCategory.Analytics, CookieStorage.SessionStorage, us, "Identificador anónimo de cada visita, para contar sesiones.", "Hasta cerrar la pestaña", 20));
    }

    private static object Seed(int n, string name, CookieCategory category, CookieStorage storage, string provider, string purpose, string duration, int order) => new
    {
        Id = new Guid($"01a16f00-0000-7000-8000-0000000001{n:D2}"),
        Name = name,
        Category = category,
        Storage = storage,
        Provider = provider,
        Purpose = purpose,
        Duration = duration,
        SortOrder = order,
    };
}

internal sealed class CookiePolicyConfiguration : IEntityTypeConfiguration<CookiePolicy>
{
    public void Configure(EntityTypeBuilder<CookiePolicy> builder)
    {
        builder.ToTable("CookiePolicies");
        builder.ConfigureAggregate();
        builder.Property(p => p.Version).HasMaxLength(40).IsRequired();
        builder.HasData(new
        {
            Id = CookiePolicy.SingletonId,
            Version = CookiePolicy.InitialVersion,
            UpdatedAt = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero),
        });
    }
}

internal sealed class CookieConsentConfiguration : IEntityTypeConfiguration<CookieConsent>
{
    public void Configure(EntityTypeBuilder<CookieConsent> builder)
    {
        builder.ToTable("CookieConsents");
        builder.ConfigureAggregate();
        builder.Property(c => c.VisitorId).HasMaxLength(CookieConsent.VisitorIdLength).IsRequired();
        builder.Property(c => c.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.PolicyVersion).HasMaxLength(40).IsRequired();
        builder.HasIndex(c => new { c.VisitorId, c.CreatedAt });
        builder.HasIndex(c => c.CreatedAt);
    }
}

internal sealed class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> builder)
    {
        builder.ToTable("AnalyticsEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityColumn();
        builder.Property(e => e.VisitorId).HasMaxLength(36).IsRequired();
        builder.Property(e => e.SessionId).HasMaxLength(36).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Path).HasMaxLength(AnalyticsEvent.MaxPath).IsRequired();
        builder.Property(e => e.RouteName).HasMaxLength(60);
        builder.Property(e => e.Label).HasMaxLength(AnalyticsEvent.MaxLabel);
        builder.Property(e => e.EntityType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Actor).HasConversion<string>().HasMaxLength(20);

        // Los informes filtran siempre por fechas y, a veces, por club (de quien actúa o de la página vista).
        builder.HasIndex(e => new { e.OccurredAt, e.Type });
        builder.HasIndex(e => new { e.ActorClubId, e.OccurredAt });
        builder.HasIndex(e => new { e.SubjectClubId, e.OccurredAt });
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}
