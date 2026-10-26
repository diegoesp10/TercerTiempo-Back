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
    // Tanda en curso: peticiones seguidas desde el último descanso y la última que salió.
    public int BurstRequests { get; set; }
    public DateTimeOffset? LastRequestAt { get; set; }
    // Cambia cada vez que toca empezar con una sesión nueva (cookies vacías); cada proceso lo compara con el suyo.
    public int SessionEpoch { get; set; }
    // Por qué está en pausa (PauseKinds) y el detalle para la administración.
    public string? PauseKind { get; set; }
    public string? PauseReason { get; set; }
    // Rechazos seguidos (páginas vacías también con sesión nueva); vuelve a 0 con la primera página buena.
    public int RejectionStreak { get; set; }
}

internal static class PauseKinds
{
    public const string Rest = "rest";
    public const string Session = "session";
    public const string Retry = "retry";
    public const string Blocked = "blocked";
    public const string Error = "error";
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
        b.Property(x => x.PauseKind).HasMaxLength(20);
        b.Property(x => x.PauseReason).HasMaxLength(500);
    }
}

internal sealed record FederationResponse(byte[]? Content, string? ContentType);
internal sealed record FederationReservation(FederationResponse? Cached = null, DateTimeOffset? RetryAt = null, bool Spacing = false, int SessionEpoch = 0);

// Pausa de una web y su motivo; Kind es uno de PauseKinds.
internal sealed record ProviderPause(DateTimeOffset Until, string Kind, string Reason, bool NewSession = false);

// Límites de una web (su fila en FederationRequestStates): hoy solo asturfutbol.es. Además de los topes por hora y por
// día, las peticiones van en tandas de MaxRequestsPerBurst con un descanso de BurstRestMinutes entre una y otra.
internal sealed record SourceLimits(
    string Provider, bool Enabled, int RequestDelayMs, int MaxRequestsPerHour, int MaxRequestsPerDay, int MaxRequestsPerBurst = 0, int BurstRestMinutes = 0);

internal interface IAsturfutbolRequestStore
{
    Task<FederationReservation> ReserveAsync(string key, string url, Guid lease, bool redirect, DateTimeOffset now, SourceLimits limits, CancellationToken ct);
    Task CompleteAsync(string provider, string key, FederationResponse response, DateTimeOffset expiresAt, CancellationToken ct);
    Task FailAsync(string provider, string key, int? status, DateTimeOffset retryAt, ProviderPause? pause, CancellationToken ct);
    Task<ProviderPause> RejectAsync(string provider, string key, int? status, string what, DateTimeOffset now, TimeSpan firstPause, TimeSpan maxPause, CancellationToken ct);
}

// Cada operación usa un contexto propio: reservar una llamada nunca guarda cambios de los agregados del solicitante.
// El bloqueo SQL coordina también dos procesos que compartan esta base de datos y su IP de salida. Cada web tiene sus
// propios contadores y pausa; la caché de páginas es común (la clave es la URL).
internal sealed class SqlAsturfutbolRequestStore(IServiceScopeFactory scopes) : IAsturfutbolRequestStore
{
    public async Task<FederationReservation> ReserveAsync(string key, string url, Guid lease, bool redirect, DateTimeOffset now, SourceLimits limits, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var state = await db.Set<FederationRequestState>()
                .FromSqlInterpolated($"SELECT * FROM [FederationRequestStates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {limits.Provider}")
                .SingleOrDefaultAsync(ct);
            if (state is null) { state = new() { Id = limits.Provider }; db.Add(state); }
            var cached = await db.Set<FederationHttpCache>().SingleOrDefaultAsync(x => x.Key == key, ct);
            if (!redirect && cached is { StatusCode: 200 or 404 } && cached.ExpiresAt > now)
                return new(Cached: new(cached.Content, cached.ContentType));
            if (!limits.Enabled) return new(RetryAt: now.AddHours(24));
            if (state.PausedUntil > now) return new(RetryAt: state.PausedUntil);
            if (cached?.NextAttemptAt > now) return new(RetryAt: cached.NextAttemptAt);
            if (cached?.LeaseUntil > now && cached.LeaseId != lease) return new(RetryAt: cached.LeaseUntil);

            var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
            var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
            if (state.HourStartedAt != hour) { state.HourStartedAt = hour; state.HourRequests = 0; }
            if (state.DayStartedAt != day) { state.DayStartedAt = day; state.DayRequests = 0; }
            if (state.DayRequests >= limits.MaxRequestsPerDay) return new(RetryAt: day.AddDays(1));
            if (state.HourRequests >= limits.MaxRequestsPerHour) return new(RetryAt: hour.AddHours(1));
            if (state.NextRequestAt > now) return new(RetryAt: state.NextRequestAt, Spacing: true);

            // Tras un descanso (o tanto tiempo sin pedir nada como un descanso) empieza otra tanda con una sesión nueva. Los
            // saltos de una redirección son parte de la misma página: no se corta a mitad ni se cambia de sesión.
            var rest = TimeSpan.FromMinutes(Math.Max(1, limits.BurstRestMinutes));
            if (!redirect && limits.MaxRequestsPerBurst > 0)
            {
                if (state.BurstRequests > 0 && (state.LastRequestAt is null || now - state.LastRequestAt >= rest))
                {
                    state.BurstRequests = 0;
                    state.SessionEpoch++;
                }
                if (state.BurstRequests >= limits.MaxRequestsPerBurst)
                {
                    state.PausedUntil = now + rest;
                    state.PauseKind = PauseKinds.Rest;
                    state.PauseReason = $"Descanso tras una tanda de {state.BurstRequests} peticiones; la siguiente empieza con una sesión nueva.";
                    state.BurstRequests = 0;
                    state.SessionEpoch++;
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return new(RetryAt: state.PausedUntil);
                }
            }

            state.HourRequests++;
            state.DayRequests++;
            state.BurstRequests++;
            state.LastRequestAt = now;
            state.NextRequestAt = now.AddMilliseconds(Math.Max(1000, limits.RequestDelayMs));
            if (cached is null) { cached = new() { Key = key, Url = url }; db.Add(cached); }
            cached.LeaseId = lease;
            cached.LeaseUntil = now.AddMinutes(5);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new FederationReservation(SessionEpoch: state.SessionEpoch);
        });
    }

    public async Task CompleteAsync(string provider, string key, FederationResponse response, DateTimeOffset expiresAt, CancellationToken ct)
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
        await db.Set<FederationRequestState>()
            .Where(s => s.Id == provider && s.RejectionStreak > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RejectionStreak, 0), ct);
    }

    public async Task FailAsync(string provider, string key, int? status, DateTimeOffset retryAt, ProviderPause? pause, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var state = await db.Set<FederationRequestState>()
                .FromSqlInterpolated($"SELECT * FROM [FederationRequestStates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {provider}")
                .SingleAsync(ct);
            var cached = await db.Set<FederationHttpCache>().SingleAsync(x => x.Key == key, ct);
            cached.StatusCode = status;
            cached.NextAttemptAt = retryAt;
            cached.LeaseId = null;
            cached.LeaseUntil = null;
            if (pause is not null && (state.PausedUntil is null || state.PausedUntil < pause.Until))
            {
                state.PausedUntil = pause.Until;
                state.PauseKind = pause.Kind;
                state.PauseReason = pause.Reason.Length > 500 ? pause.Reason[..500] : pause.Reason;
            }
            if (pause?.NewSession == true)
            {
                state.BurstRequests = 0;
                state.SessionEpoch++;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }

    // Una página que sigue vacía tras reintentarla con sesiones nuevas pausa la web un rato que se dobla con cada
    // rechazo seguido (firstPause, 2×, 4×… hasta maxPause): un fallo pasajero se recupera enseguida y uno que dura no
    // se martillea.
    public async Task<ProviderPause> RejectAsync(
        string provider, string key, int? status, string what, DateTimeOffset now, TimeSpan firstPause, TimeSpan maxPause, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var state = await db.Set<FederationRequestState>()
                .FromSqlInterpolated($"SELECT * FROM [FederationRequestStates] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {provider}")
                .SingleAsync(ct);
            state.RejectionStreak++;
            var wait = TimeSpan.FromTicks(Math.Min(maxPause.Ticks, firstPause.Ticks << Math.Min(16, state.RejectionStreak - 1)));
            var reason = state.RejectionStreak == 1
                ? $"La federación devolvió {what} también con sesiones nuevas; parece un fallo pasajero, no un bloqueo."
                : $"La federación lleva {state.RejectionStreak} intentos seguidos devolviendo {what}; se espera cada vez más entre intentos (ahora {Describe(wait)}).";
            var pause = new ProviderPause(now + wait, PauseKinds.Retry, reason, NewSession: true);

            var cached = await db.Set<FederationHttpCache>().SingleAsync(x => x.Key == key, ct);
            cached.StatusCode = status;
            cached.NextAttemptAt = pause.Until;
            cached.LeaseId = null;
            cached.LeaseUntil = null;
            if (state.PausedUntil is null || state.PausedUntil < pause.Until)
            {
                state.PausedUntil = pause.Until;
                state.PauseKind = pause.Kind;
                state.PauseReason = reason;
            }
            state.BurstRequests = 0;
            state.SessionEpoch++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return pause;
        });
    }

    private static string Describe(TimeSpan wait) =>
        wait.TotalMinutes < 60 ? $"{(int)wait.TotalMinutes} min" : wait.TotalMinutes % 60 == 0 ? $"{(int)wait.TotalHours} h" : $"{(int)wait.TotalHours} h {wait.Minutes} min";
}

internal sealed class AsturfutbolRequestGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

// Cookies de la sesión con la federación (las comparte el HttpClientHandler) y las páginas que ha servido bien. Cuando
// la base de datos dice que toca sesión nueva (otra época), se tiran las cookies y la siguiente petición vuelve a pasar
// por NLogin como cualquier visita nueva.
internal sealed class AsturfutbolSession
{
    private int _epoch = int.MinValue;

    public System.Net.CookieContainer Cookies { get; } = new();
    public int PagesServed { get; private set; }

    public void Use(int epoch)
    {
        if (epoch == _epoch) return;
        if (_epoch != int.MinValue)
        {
            foreach (System.Net.Cookie cookie in Cookies.GetAllCookies()) cookie.Expired = true;
            PagesServed = 0;
        }
        _epoch = epoch;
    }

    public void Served() => PagesServed++;

    // Sesión nueva ya, sin esperar a otra época: para repetir una página que ha llegado vacía.
    public void Reset()
    {
        foreach (System.Net.Cookie cookie in Cookies.GetAllCookies()) cookie.Expired = true;
        PagesServed = 0;
    }
}
