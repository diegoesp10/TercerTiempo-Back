using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Licensing;

namespace TercerTiempo.Application.Alerts;

public sealed record AlertDto(
    Guid Id,
    AlertKind Kind,
    string Title,
    string Message,
    [property: Description("Ruta del frontal a la que lleva el aviso.")]
    string? Link,
    [property: Description("Día al que se refiere: el último con acceso en los de caducidad; el del partido o del entrenamiento en los suyos.")]
    DateOnly? DueOn,
    DateTimeOffset CreatedAt,
    bool Read,
    [property: Description("true si ya no aplica (por ejemplo, el club reactivó la renovación o el jugador cambió su respuesta).")]
    bool Resolved);

public sealed record ClubAlertsDto(
    [property: Description("Avisos sin leer que siguen vigentes: el número de la campanita.")]
    int Unread,
    [property: Description("Los últimos avisos de la cuenta, del más reciente al más antiguo.")]
    IReadOnlyList<AlertDto> Alerts);

public sealed record GetClubAlertsQuery : IQuery<ClubAlertsDto>;

public sealed record MarkAlertReadCommand(Guid AlertId) : ICommand<ClubAlertsDto>;

public sealed record MarkAllAlertsReadCommand : ICommand<ClubAlertsDto>;

public sealed record SyncLicenseAlertsCommand : ICommand<LicenseAlertsSynced>;

public sealed record LicenseAlertsSynced(int Raised, int Resolved, int Purged = 0);

internal static class ClubAlerts
{
    public const int Shown = 30;

    public static ClubAlertsDto ToDto(IEnumerable<Alert> alerts)
    {
        var list = alerts.OrderByDescending(a => a.CreatedAt).ToList();
        return new ClubAlertsDto(
            list.Count(a => !a.IsRead && !a.IsResolved),
            list.Take(Shown)
                .Select(a => new AlertDto(a.Id, a.Kind, a.Title, a.Message, a.Link, a.DueOn, a.CreatedAt, a.IsRead, a.IsResolved))
                .ToList());
    }

    public static ClubAlertsDto ToDto(AlertPage page) =>
        new(page.Unread, page.Latest
            .Select(a => new AlertDto(a.Id, a.Kind, a.Title, a.Message, a.Link, a.DueOn, a.CreatedAt, a.IsRead, a.IsResolved))
            .ToList());

    // La campanita de la cuenta que mira: el club, el jugador o el técnico que lleva en el token. El administrador no
    // tiene. Se cuentan los sin leer en SQL y solo se leen los últimos que se enseñan.
    public static async Task<ClubAlertsDto> MineAsync(ICurrentUser user, IAlertStore alerts, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated) throw new UnauthenticatedException();
        return ToDto(await alerts.GetPageForAsync(user.ClubId, user.PlayerId, user.CoachId, Shown, cancellationToken));
    }
}

internal sealed class GetClubAlertsHandler(ICurrentUser currentUser, IAlertStore alerts) : IQueryHandler<GetClubAlertsQuery, ClubAlertsDto>
{
    public Task<ClubAlertsDto> Handle(GetClubAlertsQuery query, CancellationToken cancellationToken) =>
        ClubAlerts.MineAsync(currentUser, alerts, cancellationToken);
}

internal sealed class MarkAlertReadHandler(ICurrentUser currentUser, IAlertStore alerts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<MarkAlertReadCommand, ClubAlertsDto>
{
    public async Task<ClubAlertsDto> Handle(MarkAlertReadCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthenticatedException();
        var alert = await alerts.GetByIdAsync(command.AlertId, cancellationToken);
        if (alert is null || !alert.IsFor(currentUser.ClubId, currentUser.PlayerId, currentUser.CoachId))
            throw new NotFoundException(ErrorCode.AlertNotFound, command.AlertId);

        alert.MarkRead(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ClubAlerts.MineAsync(currentUser, alerts, cancellationToken);
    }
}

internal sealed class MarkAllAlertsReadHandler(ICurrentUser currentUser, IAlertStore alerts, TimeProvider clock)
    : ICommandHandler<MarkAllAlertsReadCommand, ClubAlertsDto>
{
    public async Task<ClubAlertsDto> Handle(MarkAllAlertsReadCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthenticatedException();
        await alerts.MarkAllReadAsync(currentUser.ClubId, currentUser.PlayerId, currentUser.CoachId, clock.GetUtcNow(), cancellationToken);
        return await ClubAlerts.MineAsync(currentUser, alerts, cancellationToken);
    }
}

// Avisos de caducidad: cuando la licencia de un club acaba sin renovarse (la prueba sin contratar o la renovación
// cancelada), se le avisa en su panel los días antes que diga la configuración (15 y 5 por defecto). Solo se crea el
// aviso del plazo más corto que ya se ha alcanzado, una vez por fecha de fin; los que dejan de aplicar (se renueva,
// caducó o cambió la fecha) se dan por resueltos.
internal sealed class LicenseAlertSync(IAlertStore alerts, ILicensingCatalog catalog, TimeProvider clock)
{
    public async Task<LicenseAlertsSynced> SyncAsync(Club club, ClubLicense license, IReadOnlyList<Alert>? clubAlerts, CancellationToken cancellationToken)
    {
        var settings = await catalog.GetAsync(cancellationToken);
        var existing = clubAlerts ?? await alerts.GetByClubAsync(club.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var today = SquadAccess.Today(clock);

        var ending = license.StatusOn(today) != LicenseStatus.Expired && !license.IsRenewing;
        var resolved = 0;
        foreach (var alert in existing.Where(a => a.Kind == AlertKind.LicenseExpiring && !a.IsResolved && (!ending || a.DueOn != license.ValidUntil)))
        {
            alert.Resolve(now);
            resolved++;
        }
        if (!ending) return new LicenseAlertsSynced(0, resolved);

        var daysLeft = license.ValidUntil.DayNumber - today.DayNumber;
        var reached = settings.ExpiryAlertDays.Where(d => daysLeft <= d).Order().ToList();
        if (reached.Count == 0) return new LicenseAlertsSynced(0, resolved);

        var key = $"license-expiring:{license.ValidUntil:yyyy-MM-dd}:{reached[0]}";
        if (existing.Any(a => a.Key == key)) return new LicenseAlertsSynced(0, resolved);

        alerts.Add(Raise(club, license, key, daysLeft, now));
        return new LicenseAlertsSynced(1, resolved);
    }

    private static Alert Raise(Club club, ClubLicense license, string key, int daysLeft, DateTimeOffset now)
    {
        var when = daysLeft switch { 0 => "hoy", 1 => "mañana", _ => $"en {daysLeft} días" };
        var end = license.ValidUntil.ToString("dd/MM/yyyy");
        return license.IsContracted
            ? Alert.ForClub(club.Id, AlertKind.LicenseExpiring, key,
                $"Tu licencia acaba {when}",
                $"La licencia de {club.Name} no se renueva y su último día es el {end}. Reactiva la renovación para que tu cuerpo técnico y tus jugadores sigan viendo sus datos.",
                "/licencias", license.ValidUntil, now)
            : Alert.ForClub(club.Id, AlertKind.LicenseExpiring, key,
                $"Tu prueba acaba {when}",
                $"La prueba de {club.Name} termina el {end}. Contrata la licencia de club para no perder el acceso a los datos privados de tus equipos; no se cobra nada hasta que acabe.",
                "/licencias", license.ValidUntil, now);
    }
}

// El trabajo de los avisos: repasa la licencia de cada club y borra los avisos resueltos hace más de PurgeAfter (ya no
// salen en la campanita: solo se enseñan los últimos y los resueltos no cuentan).
internal sealed class SyncLicenseAlertsHandler(
    IClubStore clubs,
    ILicenseStore licenses,
    IAlertStore alerts,
    LicenseAlertSync sync,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncLicenseAlertsCommand, LicenseAlertsSynced>
{
    public static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(90);

    public async Task<LicenseAlertsSynced> Handle(SyncLicenseAlertsCommand command, CancellationToken cancellationToken)
    {
        var purged = await alerts.DeleteResolvedBeforeAsync(clock.GetUtcNow() - PurgeAfter, cancellationToken);
        var byClub = (await alerts.GetByKindAsync(AlertKind.LicenseExpiring, cancellationToken)).ToLookup(a => a.ClubId);
        int raised = 0, resolved = 0;
        foreach (var license in await licenses.GetAllAsync(cancellationToken))
        {
            if (await clubs.GetByIdAsync(license.ClubId, cancellationToken) is not { } club) continue;
            var result = await sync.SyncAsync(club, license, byClub[club.Id].ToList(), cancellationToken);
            raised += result.Raised;
            resolved += result.Resolved;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LicenseAlertsSynced(raised, resolved, purged);
    }
}
