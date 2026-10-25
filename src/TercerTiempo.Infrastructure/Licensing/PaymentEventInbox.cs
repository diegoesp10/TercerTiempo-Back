using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Licensing;

// Un aviso de la pasarela en la cola (tabla PaymentEvents).
internal sealed class PaymentEvent
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public Guid? LicenseId { get; set; }
    public string Status { get; set; } = PaymentEventStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public string? Summary { get; set; }
}

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> b)
    {
        b.ToTable("PaymentEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(120).IsRequired();
        b.Property(x => x.Type).HasMaxLength(80).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Error).HasMaxLength(1000);
        b.Property(x => x.Summary).HasMaxLength(500);
        b.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => new { x.LicenseId, x.ReceivedAt });
    }
}

internal sealed class PaymentEventInbox(AppDbContext db, TimeProvider clock) : IPaymentEventInbox
{
    public async Task<bool> AddAsync(PaymentEventInput input, CancellationToken cancellationToken)
    {
        if (await db.Set<PaymentEvent>().AnyAsync(e => e.Provider == input.Provider && e.ExternalId == input.ExternalId, cancellationToken))
            return false;

        db.Add(new PaymentEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = input.Provider,
            ExternalId = input.ExternalId,
            Type = input.Type,
            Payload = input.Payload,
            LicenseId = input.LicenseId,
            ReceivedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Dos entregas del mismo aviso a la vez: la otra ya lo ha guardado.
            return false;
        }
    }

    public async Task<IReadOnlyList<PaymentEventDto>> ListAsync(int max, Guid? licenseId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var query = db.Set<PaymentEvent>().AsNoTracking();
        if (licenseId is { } id) query = query.Where(e => e.LicenseId == id);
        if (since is { } from) query = query.Where(e => e.ReceivedAt >= from);

        var rows = await query.OrderByDescending(e => e.ReceivedAt).Take(max).ToListAsync(cancellationToken);
        var licenseIds = rows.Select(r => r.LicenseId).OfType<Guid>().Distinct().ToList();
        var clubs = await db.ClubLicenses.AsNoTracking()
            .Where(l => licenseIds.Contains(l.Id))
            .Join(db.Clubs, l => l.ClubId, c => c.Id, (l, c) => new { l.Id, c.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return rows.Select(r => new PaymentEventDto(
            r.Id, r.Provider, r.ExternalId, r.Type, r.Status, r.Attempts, r.ReceivedAt, r.ProcessedAt, r.NextAttemptAt, r.Error,
            r.LicenseId, r.LicenseId is { } l ? clubs.GetValueOrDefault(l) : null, r.Summary)).ToList();
    }

    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Set<PaymentEvent>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (row is null) return false;
        row.Status = PaymentEventStatus.Pending;
        row.NextAttemptAt = null;
        row.Error = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed class PaymentEventSignal : IPaymentEventSignal
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Signal()
    {
        lock (_lock) _signal.TrySetResult();
    }

    public Task WhenSignalled()
    {
        lock (_lock)
        {
            if (_signal.Task.IsCompleted) _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _signal.Task;
        }
    }
}

// Aplica los avisos de la cola en orden de llegada. Si uno falla (la base de datos no responde, un error puntual), se
// reintenta cada vez más espaciado; tras agotar los intentos queda como fallido para mirarlo desde la administración.
internal sealed class PaymentEventWorker(
    IServiceScopeFactory scopes,
    PaymentEventSignal signal,
    TimeProvider clock,
    ILogger<PaymentEventWorker> logger) : BackgroundService
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6)];
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wake = signal.WhenSignalled();
            try
            {
                while (await NextAsync(stoppingToken) is { } id) await ProcessAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en la cola de eventos de la pasarela");
            }

            try { await Task.WhenAny(wake, Task.Delay(Idle, clock, stoppingToken)); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<Guid?> NextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        return await db.Set<PaymentEvent>().AsNoTracking()
            .Where(e => e.Status == PaymentEventStatus.Pending && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.ReceivedAt)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        string status;
        string? summary = null;
        string? error = null;
        Guid? licenseId = null;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<PaymentEvent>().AsNoTracking().SingleAsync(e => e.Id == id, cancellationToken);
            if (StripeEvents.Parse(row.Payload) is not { } parsed)
            {
                (status, summary) = (PaymentEventStatus.Ignored, "Tipo de evento que no cambia las licencias");
            }
            else
            {
                var outcome = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyLicenseEventCommand, LicenseEventOutcome>>()
                    .Handle(new ApplyLicenseEventCommand(parsed), cancellationToken);
                (status, summary, licenseId) = (outcome.Applied ? PaymentEventStatus.Processed : PaymentEventStatus.Ignored, outcome.Summary, outcome.LicenseId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo aplicar el evento de la pasarela {EventId}", id);
            status = PaymentEventStatus.Pending;
            error = ex.Message;
        }

        // Otro contexto: si aplicar el evento falló a medias, sus cambios no se guardan con el estado.
        using var markScope = scopes.CreateScope();
        var markDb = markScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mark = await markDb.Set<PaymentEvent>().SingleAsync(e => e.Id == id, cancellationToken);
        var now = clock.GetUtcNow();
        mark.Attempts++;
        mark.Summary = summary;
        mark.LicenseId ??= licenseId;
        if (error is null)
        {
            mark.Status = status;
            mark.ProcessedAt = now;
            mark.Error = null;
            mark.NextAttemptAt = null;
        }
        else
        {
            mark.Error = error.Length > 1000 ? error[..1000] : error;
            if (mark.Attempts > Backoff.Length)
            {
                mark.Status = PaymentEventStatus.Failed;
                mark.NextAttemptAt = null;
            }
            else
            {
                mark.NextAttemptAt = now + Backoff[mark.Attempts - 1];
            }
        }
        await markDb.SaveChangesAsync(cancellationToken);
    }
}
