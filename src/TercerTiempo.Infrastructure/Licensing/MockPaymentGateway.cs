using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;
using TercerTiempo.Infrastructure.Persistence;

namespace TercerTiempo.Infrastructure.Licensing;

// Sesión de pago de la pasarela simulada (tabla MockCheckoutSessions): lo que en Stripe sería la Checkout Session.
internal sealed class MockCheckoutSession
{
    public string SessionId { get; set; } = "";
    public Guid LicenseId { get; set; }
    public string Status { get; set; } = MockSessionStatus.Open;
    public int AmountCents { get; set; }
    public int PeriodPriceCents { get; set; }
    public string Currency { get; set; } = "EUR";
    public int Months { get; set; }
    public DateOnly? DeferUntil { get; set; }
    public string? CustomerRef { get; set; }
    public string? SubscriptionRef { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public int Attempts { get; set; }
    public string? LastDecline { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

internal static class MockSessionStatus
{
    public const string Open = "open";
    public const string Complete = "complete";
}

internal sealed class MockCheckoutSessionConfiguration : IEntityTypeConfiguration<MockCheckoutSession>
{
    public void Configure(EntityTypeBuilder<MockCheckoutSession> b)
    {
        b.ToTable("MockCheckoutSessions");
        b.HasKey(x => x.SessionId);
        b.Property(x => x.SessionId).HasMaxLength(120);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.CustomerRef).HasMaxLength(120);
        b.Property(x => x.SubscriptionRef).HasMaxLength(120);
        b.Property(x => x.LastDecline).HasMaxLength(60);
        b.HasIndex(x => x.LicenseId);
    }
}

// Pasarela simulada (Licensing:Provider = "mock"): hace de Stripe sin cobrar nada. Sus sesiones de pago se guardan y se
// completan desde la página de pago con las tarjetas de prueba de Stripe; al completarse, y en cada renovación, cobro
// rechazado o baja, deja en la cola de eventos los mismos avisos que mandaría Stripe, con su formato, y la cola los
// aplica igual que los de verdad. Stripe ocupa su sitio con Licensing:Provider = "stripe" sin tocar el resto.
internal sealed class MockPaymentGateway(
    AppDbContext db,
    IPaymentEventInbox inbox,
    IPaymentEventSignal signal,
    ILicensingCatalog catalog,
    TimeProvider clock) : ILicensePaymentGateway, IPaymentSimulator
{
    public const string Name = "mock";

    public string Provider => Name;

    public bool RenewsByWebhook => false;

    public bool Enabled => true;

    public async Task<CheckoutSession> BeginCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var sessionId = StripeEvents.NewId("cs_mock");
        db.Add(new MockCheckoutSession
        {
            SessionId = sessionId,
            LicenseId = request.LicenseId,
            AmountCents = request.DeferUntil is null ? request.PeriodPriceCents : 0,
            PeriodPriceCents = request.PeriodPriceCents,
            Currency = request.Currency,
            Months = request.Interval.Months(),
            DeferUntil = request.DeferUntil,
            CustomerRef = request.CustomerRef,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return new CheckoutSession(sessionId, $"/pago/{sessionId}");
    }

    public async Task<CheckoutResult> GetCheckoutAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await db.Set<MockCheckoutSession>().AsNoTracking().SingleOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
        return session is { Status: MockSessionStatus.Complete }
            ? new CheckoutResult(true, session.SubscriptionRef, session.CustomerRef, session.PeriodEnd)
            : new CheckoutResult(false, null, null, null);
    }

    public Task CancelRenewalAsync(string subscriptionRef, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResumeRenewalAsync(string subscriptionRef, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ChangeRenewalAsync(string subscriptionRef, BillingInterval interval, int staffSeats, CancellationToken cancellationToken) => Task.CompletedTask;

    // Como Stripe con always_invoice: los técnicos añadidos con un periodo pagado se cobran ya con una factura
    // «subscription_update» por su parte proporcional.
    public async Task ChangeStaffSeatsAsync(StaffSeatsChange change, CancellationToken cancellationToken)
    {
        if (change.ChargeNowCents <= 0) return;
        var now = clock.GetUtcNow();
        var settings = await catalog.GetAsync(cancellationToken);
        var eventId = StripeEvents.NewId("evt_mock");
        await inbox.AddAsync(new PaymentEventInput(Name, eventId, "invoice.paid",
            StripeEvents.InvoicePaid(eventId, StripeEvents.NewId("in_mock"), change.SubscriptionRef, change.LicenseId, "subscription_update",
                change.ChargeNowCents, settings.Currency, change.PeriodEnd, now), change.LicenseId), cancellationToken);
        signal.Signal();
    }

    // A la simulada no le llegan avisos de fuera: los suyos los deja ella misma en la cola.
    public ReceivedWebhook ReadWebhook(string payload, string? signature) => throw new DomainException(ErrorCode.LicenseWebhookInvalid);

    public async Task<SimulatedPaymentDto> PayCheckoutAsync(LicensePayment payment, ClubLicense license, SimulatedPaymentOutcome outcome, CancellationToken cancellationToken)
    {
        var session = await db.Set<MockCheckoutSession>().SingleOrDefaultAsync(s => s.SessionId == payment.SessionId, cancellationToken)
            ?? Legacy(payment, license);
        if (session.Status == MockSessionStatus.Complete || payment.IsPaid)
            return new SimulatedPaymentDto(true, null, "Este pago ya estaba completado.");

        session.Attempts++;
        if (outcome != SimulatedPaymentOutcome.Succeeded)
        {
            var (code, message) = Decline(outcome);
            session.LastDecline = code;
            await db.SaveChangesAsync(cancellationToken);
            return new SimulatedPaymentDto(false, code, message);
        }

        var now = clock.GetUtcNow();
        var today = Domain.Squads.Squad.Today(clock.GetUtcNow());
        session.Status = MockSessionStatus.Complete;
        session.CompletedAt = now;
        session.LastDecline = null;
        session.CustomerRef ??= license.CustomerRef ?? StripeEvents.NewId("cus_mock");
        session.SubscriptionRef = StripeEvents.NewId("sub_mock");
        // Sin prueba ni periodo pagado por delante se cobra ya el primer periodo.
        if (session.AmountCents > 0) session.PeriodEnd = today.AddMonths(session.Months);
        await db.SaveChangesAsync(cancellationToken);

        // Lo que mandaría Stripe: el alta completada y, si se ha cobrado, la primera factura.
        var completedId = StripeEvents.NewId("evt_mock");
        await inbox.AddAsync(new PaymentEventInput(Name, completedId, "checkout.session.completed",
            StripeEvents.CheckoutCompleted(completedId, session.SessionId, session.SubscriptionRef, session.CustomerRef, now), license.Id), cancellationToken);
        if (session.AmountCents > 0 && session.PeriodEnd is { } periodEnd)
        {
            var eventId = StripeEvents.NewId("evt_mock");
            await inbox.AddAsync(new PaymentEventInput(Name, eventId, "invoice.paid",
                StripeEvents.InvoicePaid(eventId, StripeEvents.NewId("in_mock"), session.SubscriptionRef, license.Id, "subscription_create",
                    session.AmountCents, session.Currency, periodEnd, now), license.Id), cancellationToken);
        }
        signal.Signal();
        return new SimulatedPaymentDto(true, null, "Pago aceptado.");
    }

    public async Task<string> EmitAsync(ClubLicense license, SimulatedLicenseEvent kind, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = Domain.Squads.Squad.Today(clock.GetUtcNow());
        var subscription = license.SubscriptionRef ?? throw new DomainException(ErrorCode.LicenseNoSubscription);
        var settings = await catalog.GetAsync(cancellationToken);
        var interval = license.NextInterval ?? license.Interval ?? BillingInterval.Monthly;
        var price = settings.SubscriptionPriceCents(interval, license.RenewalStaffSeats);
        // Una factura por periodo: la de la renovación que toca (cobrarla dos veces o reintentarla es el mismo id).
        var renewsOn = license.RenewsOn ?? license.ValidUntil.AddDays(1);
        var invoiceId = $"in_mock_{license.Id:N}_{renewsOn:yyyyMMdd}";
        string eventId;
        string description;

        switch (kind)
        {
            case SimulatedLicenseEvent.RenewalPaid:
                eventId = $"evt_mock_{invoiceId}_paid";
                await inbox.AddAsync(new PaymentEventInput(Name, eventId, "invoice.paid",
                    StripeEvents.InvoicePaid(eventId, invoiceId, subscription, license.Id, "subscription_cycle", price, settings.Currency,
                        license.RenewalPeriodEnd(interval, today), now), license.Id), cancellationToken);
                description = "Renovación cobrada (invoice.paid) en la cola";
                break;

            case SimulatedLicenseEvent.RenewalFailed:
                var prefix = $"evt_mock_{invoiceId}_failed_";
                var attempt = await db.Set<PaymentEvent>().CountAsync(e => e.Provider == Name && e.ExternalId.StartsWith(prefix), cancellationToken) + 1;
                // Como Stripe: unos cuantos reintentos espaciados y, tras el último, sin más intentos.
                DateTimeOffset? next = attempt < 4 ? now.AddDays(attempt == 1 ? 3 : attempt == 2 ? 5 : 7) : null;
                eventId = $"{prefix}{attempt}";
                await inbox.AddAsync(new PaymentEventInput(Name, eventId, "invoice.payment_failed",
                    StripeEvents.InvoicePaymentFailed(eventId, invoiceId, subscription, license.Id, price, settings.Currency,
                        license.RenewalPeriodEnd(interval, today), attempt, next, "card_declined", "Tarjeta rechazada por la entidad (simulado)", now), license.Id), cancellationToken);
                description = $"Cobro rechazado, intento {attempt} (invoice.payment_failed) en la cola";
                break;

            default:
                eventId = $"evt_mock_{subscription}_deleted";
                await inbox.AddAsync(new PaymentEventInput(Name, eventId, "customer.subscription.deleted",
                    StripeEvents.SubscriptionDeleted(eventId, subscription, license.Id, now), license.Id), cancellationToken);
                description = "Suscripción terminada (customer.subscription.deleted) en la cola";
                break;
        }

        signal.Signal();
        return description;
    }

    // Pagos abiertos antes de que la simulada guardara sus sesiones: se reconstruye la sesión con los datos del pago.
    private MockCheckoutSession Legacy(LicensePayment payment, ClubLicense license)
    {
        var session = new MockCheckoutSession
        {
            SessionId = payment.SessionId,
            LicenseId = license.Id,
            AmountCents = payment.AmountCents,
            PeriodPriceCents = payment.PeriodPriceCents,
            Currency = payment.Currency,
            Months = payment.Months,
            CustomerRef = license.CustomerRef,
            CreatedAt = payment.CreatedAt,
        };
        db.Add(session);
        return session;
    }

    // Los mismos códigos y mensajes que da Stripe Checkout con sus tarjetas de prueba.
    private static (string Code, string Message) Decline(SimulatedPaymentOutcome outcome) => outcome switch
    {
        SimulatedPaymentOutcome.InsufficientFunds => ("insufficient_funds", "Tu tarjeta no tiene fondos suficientes."),
        SimulatedPaymentOutcome.ExpiredCard => ("expired_card", "Tu tarjeta ha caducado."),
        SimulatedPaymentOutcome.AuthenticationFailed => ("authentication_failed", "No se ha podido autenticar tu método de pago. Elige otro e inténtalo de nuevo."),
        _ => ("card_declined", "Tu tarjeta ha sido rechazada."),
    };
}

// Con Stripe no hay simulador: cobra, rechaza y da de baja la propia Stripe.
internal sealed class DisabledPaymentSimulator : IPaymentSimulator
{
    public bool Enabled => false;

    public Task<SimulatedPaymentDto> PayCheckoutAsync(LicensePayment payment, ClubLicense license, SimulatedPaymentOutcome outcome, CancellationToken cancellationToken) =>
        throw new ConflictException(ErrorCode.LicenseSimulatorDisabled);

    public Task<string> EmitAsync(ClubLicense license, SimulatedLicenseEvent kind, CancellationToken cancellationToken) =>
        throw new ConflictException(ErrorCode.LicenseSimulatorDisabled);
}
