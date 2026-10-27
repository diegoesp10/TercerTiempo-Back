using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Licensing;

public static class PaymentEventStatus
{
    public const string Pending = "pending";
    public const string Processed = "processed";
    // Llegó bien pero no cambia nada (un tipo que no se usa, un aviso repetido...).
    public const string Ignored = "ignored";
    // Agotó los reintentos: hay que mirarlo a mano (se puede volver a lanzar).
    public const string Failed = "failed";
}

public sealed record PaymentEventInput(string Provider, string ExternalId, string Type, string Payload, Guid? LicenseId);

public sealed record PaymentEventDto(
    Guid Id,
    [property: Description("stripe o mock (simulada).")]
    string Provider,
    [property: Description("Id del evento en la pasarela (evt_...).")]
    string ExternalId,
    [property: Description("checkout.session.completed, invoice.paid, invoice.payment_failed, customer.subscription.deleted...")]
    string Type,
    string Status,
    int Attempts,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? NextAttemptAt,
    string? Error,
    Guid? LicenseId,
    string? ClubName,
    [property: Description("Qué hizo al aplicarse («Renovación cobrada: 9,90 €»...).")]
    string? Summary);

// Cola de avisos de la pasarela (los webhooks de Stripe o los que genera la simulada): se guardan al llegar, sin
// repetir el mismo id, y un proceso en segundo plano los aplica a las licencias con reintentos. Así Stripe recibe su
// 200 enseguida y ningún aviso se pierde si la API se para o falla la base de datos a mitad.
public interface IPaymentEventInbox
{
    // false si ese evento ya estaba en la cola.
    Task<bool> AddAsync(PaymentEventInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentEventDto>> ListAsync(int max, Guid? licenseId, DateTimeOffset? since, CancellationToken cancellationToken);
    Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken);
}

// Avisa al proceso de la cola de que hay eventos nuevos.
public interface IPaymentEventSignal
{
    void Signal();
}

public enum SimulatedPaymentOutcome
{
    Succeeded = 1,
    Declined = 2,
    InsufficientFunds = 3,
    ExpiredCard = 4,
    AuthenticationFailed = 5,
}

public enum SimulatedLicenseEvent
{
    // Factura de renovación cobrada (lo que hace Stripe al llegar el día).
    RenewalPaid = 1,
    // Cobro de la renovación rechazado; la pasarela lo reintentará.
    RenewalFailed = 2,
    // Suscripción terminada (impago tras los reintentos o baja desde la pasarela).
    SubscriptionDeleted = 3,
}

public sealed record SimulatedPaymentDto(
    bool Succeeded,
    [property: Description("Código de rechazo como el de Stripe: card_declined, insufficient_funds, expired_card, authentication_failed.")]
    string? DeclineCode,
    string Message);

// Pasarela simulada al completo: hace de Stripe Checkout (resultado del pago con las tarjetas de prueba) y genera en
// la cola los mismos eventos que mandaría Stripe, con su mismo formato. Con Stripe no está disponible.
public interface IPaymentSimulator
{
    bool Enabled { get; }
    Task<SimulatedPaymentDto> PayCheckoutAsync(LicensePayment payment, ClubLicense license, SimulatedPaymentOutcome outcome, CancellationToken cancellationToken);
    Task<string> EmitAsync(ClubLicense license, SimulatedLicenseEvent kind, CancellationToken cancellationToken);
}

public sealed record LicenseEventOutcome(bool Applied, Guid? LicenseId, string Summary);

// Aplica a la licencia un evento de la pasarela ya leído de la cola (alta completada, factura cobrada o rechazada,
// suscripción terminada). Es idempotente: aplicar dos veces el mismo evento no cambia nada.
public sealed record ApplyLicenseEventCommand(LicenseWebhookEvent Event) : ICommand<LicenseEventOutcome>;

internal sealed class ApplyLicenseEventHandler(
    ILicenseStore licenses,
    ILicensePaymentGateway gateway,
    LicenseCheckoutCompletion completion,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ApplyLicenseEventCommand, LicenseEventOutcome>
{
    public async Task<LicenseEventOutcome> Handle(ApplyLicenseEventCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        switch (command.Event)
        {
            case CheckoutCompletedEvent completed:
            {
                if (await licenses.GetPaymentBySessionAsync(completed.SessionId, cancellationToken) is not { } payment)
                    return new LicenseEventOutcome(false, null, "Sesión de pago desconocida");
                if (payment.IsPaid) return new LicenseEventOutcome(false, payment.LicenseId, "Alta ya confirmada");
                await completion.CompleteAsync(payment, cancellationToken);
                return new LicenseEventOutcome(true, payment.LicenseId, $"Suscripción dada de alta ({SubscriptionText(payment.StaffSeats, payment.Interval)})");
            }

            case InvoicePaidEvent invoice:
            {
                var license = await LicenseOfAsync(invoice.LicenseId, invoice.SubscriptionRef, cancellationToken);
                if (license is null) return new LicenseEventOutcome(false, null, "Suscripción desconocida");
                if (invoice.AmountCents == 0) return new LicenseEventOutcome(false, license.Id, "Factura de 0 € (prueba)");
                if (await licenses.GetPaymentBySessionAsync(invoice.InvoiceId, cancellationToken) is not null)
                    return new LicenseEventOutcome(false, license.Id, "Factura ya apuntada");

                // La primera factura de una suscripción sin prueba ya está apuntada como el pago del alta.
                if (invoice.BillingReason == "subscription_create")
                {
                    license.RecordPaidPeriod(invoice.PeriodEnd, now);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return new LicenseEventOutcome(true, license.Id, $"Primer cobro: pagado hasta el {invoice.PeriodEnd:dd/MM/yyyy}");
                }

                // Técnicos añadidos a mitad de periodo: su parte proporcional, sin tocar lo pagado ni los cambios pedidos.
                if (invoice.BillingReason == "subscription_update")
                {
                    var seats = LicensePayment.Begin(
                        license, LicensePaymentKind.StaffSeats, license.Interval ?? BillingInterval.Monthly, license.StaffSeats,
                        invoice.AmountCents, invoice.AmountCents, invoice.Currency, gateway.Provider, invoice.InvoiceId, now);
                    seats.MarkPaid(now);
                    licenses.AddPayment(seats);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return new LicenseEventOutcome(true, license.Id, $"Técnicos añadidos ({LicenseTexts.Staff(license.StaffSeats)} en total): {Money(invoice.AmountCents, invoice.Currency)} hasta la renovación");
                }

                license.RecordRenewal(invoice.PeriodEnd, now);
                var renewal = LicensePayment.Begin(
                    license, LicensePaymentKind.Renewal, license.Interval ?? BillingInterval.Monthly, license.StaffSeats,
                    invoice.AmountCents, invoice.AmountCents, invoice.Currency, gateway.Provider, invoice.InvoiceId, now);
                renewal.MarkPaid(now);
                licenses.AddPayment(renewal);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new LicenseEventOutcome(true, license.Id, $"Renovación cobrada: {Money(invoice.AmountCents, invoice.Currency)}, pagado hasta el {invoice.PeriodEnd:dd/MM/yyyy}");
            }

            case InvoicePaymentFailedEvent failed:
            {
                var license = await LicenseOfAsync(failed.LicenseId, failed.SubscriptionRef, cancellationToken);
                if (license is null) return new LicenseEventOutcome(false, null, "Suscripción desconocida");
                // Un apunte por intento: el mismo intento repetido no se apunta dos veces.
                var reference = $"{failed.InvoiceId}#{failed.AttemptCount}";
                if (await licenses.GetPaymentBySessionAsync(reference, cancellationToken) is not null)
                    return new LicenseEventOutcome(false, license.Id, "Intento ya apuntado");

                var seatsAdded = failed.BillingReason == "subscription_update";
                var payment = LicensePayment.Begin(
                    license, seatsAdded ? LicensePaymentKind.StaffSeats : LicensePaymentKind.Renewal,
                    (seatsAdded ? license.Interval : license.NextInterval ?? license.Interval) ?? BillingInterval.Monthly,
                    seatsAdded ? license.StaffSeats : license.RenewalStaffSeats,
                    failed.AmountCents, failed.AmountCents, failed.Currency, gateway.Provider, reference, now);
                payment.MarkFailed(failed.Reason);
                licenses.AddPayment(payment);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                var next = failed.NextAttemptAt is { } at ? $"; siguiente intento el {at.ToLocalTime():dd/MM HH:mm}" : "; sin más intentos";
                return new LicenseEventOutcome(true, license.Id, $"Cobro rechazado (intento {failed.AttemptCount}): {failed.Reason}{next}");
            }

            case SubscriptionEndedEvent ended:
            {
                if (await licenses.GetBySubscriptionAsync(ended.SubscriptionRef, cancellationToken) is not { } license)
                    return new LicenseEventOutcome(false, null, "Suscripción desconocida");
                if (!license.AutoRenew) return new LicenseEventOutcome(false, license.Id, "La suscripción ya no se renovaba");
                license.EndSubscription(now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new LicenseEventOutcome(true, license.Id, $"Suscripción terminada: el acceso acaba el {license.ValidUntil:dd/MM/yyyy}");
            }

            default:
                return new LicenseEventOutcome(false, null, "Evento que no cambia las licencias");
        }
    }

    private async Task<ClubLicense?> LicenseOfAsync(Guid? licenseId, string subscriptionRef, CancellationToken cancellationToken) =>
        (licenseId is { } id ? await licenses.GetByIdAsync(id, cancellationToken) : null)
        ?? await licenses.GetBySubscriptionAsync(subscriptionRef, cancellationToken);

    private static string SubscriptionText(int staffSeats, BillingInterval interval) =>
        $"{LicenseTexts.Subscription(staffSeats)}, {(interval == BillingInterval.Yearly ? "anual" : "mensual")}";

    internal static string Money(int cents, string currency) =>
        $"{cents / 100m:0.00} {(currency == "EUR" ? "€" : currency)}".Replace('.', ',');
}

public sealed record SimulateCheckoutPaymentCommand(
    string SessionId,
    [property: Description("Resultado de la tarjeta de prueba: Succeeded, Declined, InsufficientFunds, ExpiredCard o AuthenticationFailed.")]
    SimulatedPaymentOutcome Outcome) : ICommand<SimulatedPaymentDto>;

// Pago en la página de la pasarela simulada: lo que contestaría Stripe Checkout con cada tarjeta de prueba.
internal sealed class SimulateCheckoutPaymentHandler(
    ICurrentUser currentUser,
    ILicenseStore licenses,
    IClubStore clubs,
    IPaymentSimulator simulator) : ICommandHandler<SimulateCheckoutPaymentCommand, SimulatedPaymentDto>
{
    public async Task<SimulatedPaymentDto> Handle(SimulateCheckoutPaymentCommand command, CancellationToken cancellationToken)
    {
        if (!simulator.Enabled) throw new ConflictException(ErrorCode.LicenseSimulatorDisabled);
        var club = await Resolvers.CurrentClubAsync(currentUser, clubs, cancellationToken);
        var payment = await licenses.GetPaymentBySessionAsync(command.SessionId, cancellationToken);
        if (payment is null || payment.ClubId != club.Id)
            throw new NotFoundException(ErrorCode.LicensePaymentNotFound, command.SessionId);
        var license = await licenses.GetByIdAsync(payment.LicenseId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.LicenseNotFound, payment.LicenseId);

        return await simulator.PayCheckoutAsync(payment, license, command.Outcome, cancellationToken);
    }
}

public sealed record SimulateLicenseEventCommand(Guid LicenseId, SimulatedLicenseEvent Event) : ICommand<string>;

// Administración, solo con la pasarela simulada: provocar lo que haría Stripe con una suscripción (cobrar la
// renovación, rechazar el cobro, darla de baja) para probar cómo reacciona la app.
internal sealed class SimulateLicenseEventHandler(ILicenseStore licenses, IPaymentSimulator simulator) : ICommandHandler<SimulateLicenseEventCommand, string>
{
    public async Task<string> Handle(SimulateLicenseEventCommand command, CancellationToken cancellationToken)
    {
        if (!simulator.Enabled) throw new ConflictException(ErrorCode.LicenseSimulatorDisabled);
        var license = await licenses.GetByIdAsync(command.LicenseId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.LicenseNotFound, command.LicenseId);
        if (license.SubscriptionRef is null) throw new ConflictException(ErrorCode.LicenseNoSubscription);
        return await simulator.EmitAsync(license, command.Event, cancellationToken);
    }
}

public sealed record ListPaymentEventsQuery(Guid? LicenseId, int Max = 100) : IQuery<IReadOnlyList<PaymentEventDto>>;

internal sealed class ListPaymentEventsHandler(IPaymentEventInbox inbox) : IQueryHandler<ListPaymentEventsQuery, IReadOnlyList<PaymentEventDto>>
{
    public Task<IReadOnlyList<PaymentEventDto>> Handle(ListPaymentEventsQuery query, CancellationToken cancellationToken) =>
        inbox.ListAsync(Math.Clamp(query.Max, 1, 500), query.LicenseId, null, cancellationToken);
}

public sealed record RetryPaymentEventCommand(Guid Id) : ICommand<bool>;

internal sealed class RetryPaymentEventHandler(IPaymentEventInbox inbox, IPaymentEventSignal signal) : ICommandHandler<RetryPaymentEventCommand, bool>
{
    public async Task<bool> Handle(RetryPaymentEventCommand command, CancellationToken cancellationToken)
    {
        if (!await inbox.RetryAsync(command.Id, cancellationToken)) throw new NotFoundException(ErrorCode.LicensePaymentNotFound, command.Id);
        signal.Signal();
        return true;
    }
}
