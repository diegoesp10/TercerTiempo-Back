using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

internal sealed class FederationHttpCache
{
    public string Key { get; set; } = "";
    public string Url { get; set; } = "";
    public byte[]? Content { get; set; }
    public string? ContentType { get; set; }
    public int? StatusCode { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}

internal sealed class FederationRequestState
{
    public string Id { get; set; } = "asturfutbol";
    public DateTimeOffset NextRequestAt { get; set; }
    public DateTimeOffset? PausedUntil { get; set; }
    public DateTimeOffset HourStartedAt { get; set; }
    public int HourRequests { get; set; }
    public DateTimeOffset DayStartedAt { get; set; }
    public int DayRequests { get; set; }
}

internal sealed class FederationHttpCacheConfiguration : IEntityTypeConfiguration<FederationHttpCache>
{
    public void Configure(EntityTypeBuilder<FederationHttpCache> b)
    {
        b.ToTable("FederationHttpCache");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(64);
        b.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200);
    }
}

internal sealed class FederationRequestStateConfiguration : IEntityTypeConfiguration<FederationRequestState>
{
    public void Configure(EntityTypeBuilder<FederationRequestState> b)
    {
        b.ToTable("FederationRequestStates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(50);
    }
}

internal sealed record FederationResponse(byte[]? Content, string? ContentType);
internal sealed record FederationReservation(FederationResponse? Cached = null, DateTimeOffset? RetryAt = null, bool Spacing = false);

internal interface IAsturfutbolRequestStore
{
    Task<FederationReservation> ReserveAsync(string key, string url, Guid lease, bool redirect, DateTimeOffset now, OfficialSheetOptions options, CancellationToken ct);
    Task CompleteAsync(string key, FederationResponse response, DateTimeOffset expiresAt, CancellationToken ct);
    Task FailAsync(string key, int? status, DateTimeOffset retryAt, bool pauseProvider, CancellationToken ct);
}

// Cada operación usa un contexto propio: reservar una llamada nunca guarda cambios de los agregados del solicitante.
// El bloqueo SQL coordina también dos procesos que compartan esta base de datos y su IP de salida.
internal sealed class SqlAsturfutbolRequestStore(IServiceScopeFactory scopes) : IAsturfutbolRequestStore
{
    public async Task<FederationReservation> ReserveAsync(string key, string url, Guid lease, bool redirect, DateTimeOffset now, OfficialSheetOptions options, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var state = await db.Set<FederationRequestState>()
                .FromSqlRaw("SELECT * FROM [FederationRequestStates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 'asturfutbol'")
                .SingleOrDefaultAsync(ct);
            if (state is null) { state = new(); db.Add(state); }
            var cached = await db.Set<FederationHttpCache>().SingleOrDefaultAsync(x => x.Key == key, ct);
            if (!redirect && cached is { StatusCode: 200 or 404 } && cached.ExpiresAt > now)
                return new(Cached: new(cached.Content, cached.ContentType));
            if (!options.Enabled) return new(RetryAt: now.AddHours(24));
            if (state.PausedUntil > now) return new(RetryAt: state.PausedUntil);
            if (cached?.NextAttemptAt > now) return new(RetryAt: cached.NextAttemptAt);
            if (cached?.LeaseUntil > now && cached.LeaseId != lease) return new(RetryAt: cached.LeaseUntil);

            var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
            var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
            if (state.HourStartedAt != hour) { state.HourStartedAt = hour; state.HourRequests = 0; }
            if (state.DayStartedAt != day) { state.DayStartedAt = day; state.DayRequests = 0; }
            if (state.DayRequests >= options.Asturfutbol.MaxRequestsPerDay) return new(RetryAt: day.AddDays(1));
            if (state.HourRequests >= options.Asturfutbol.MaxRequestsPerHour) return new(RetryAt: hour.AddHours(1));
            if (state.NextRequestAt > now) return new(RetryAt: state.NextRequestAt, Spacing: true);

            state.HourRequests++;
            state.DayRequests++;
            state.NextRequestAt = now.AddMilliseconds(Math.Max(1000, options.Asturfutbol.RequestDelayMs));
            if (cached is null) { cached = new() { Key = key, Url = url }; db.Add(cached); }
            cached.LeaseId = lease;
            cached.LeaseUntil = now.AddMinutes(5);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new FederationReservation();
        });
    }

    public async Task CompleteAsync(string key, FederationResponse response, DateTimeOffset expiresAt, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cached = await db.Set<FederationHttpCache>().SingleAsync(x => x.Key == key, ct);
        cached.Content = response.Content;
        cached.ContentType = response.ContentType;
        cached.StatusCode = response.Content is null ? 404 : 200;
        cached.ExpiresAt = expiresAt;
        cached.NextAttemptAt = null;
        cached.LeaseId = null;
        cached.LeaseUntil = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(string key, int? status, DateTimeOffset retryAt, bool pauseProvider, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var state = await db.Set<FederationRequestState>()
                .FromSqlRaw("SELECT * FROM [FederationRequestStates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 'asturfutbol'")
                .SingleAsync(ct);
            var cached = await db.Set<FederationHttpCache>().SingleAsync(x => x.Key == key, ct);
            cached.StatusCode = status;
            cached.NextAttemptAt = retryAt;
            cached.LeaseId = null;
            cached.LeaseUntil = null;
            if (pauseProvider && (state.PausedUntil is null || state.PausedUntil < retryAt)) state.PausedUntil = retryAt;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}

internal sealed class AsturfutbolRequestGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
