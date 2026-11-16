using System.Globalization;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

// La citación a entrenar, para cada jugador con cuenta: no se firma (eso es la convocatoria de un partido), solo dice
// que le esperan. Un aviso por serie al crearla —no uno por sesión—, otro si una sesión cambia de día, hora o lugar y
// otro si se anula. Cada aviso nuevo da por resuelto el anterior de la misma sesión o serie.
internal static class TrainingCalls
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static string KeyPrefix(Guid sessionOrSeriesId) => $"training-call:{sessionOrSeriesId}";

    public static string Link(Guid squadId, Guid? sessionId = null) =>
        sessionId is { } id ? $"/plantillas/{squadId}/agenda?sesion={id}" : $"/plantillas/{squadId}/agenda";

    public static Task PlannedAsync(Squad squad, IReadOnlyList<TrainingSession> created, IAlertStore alerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var first = created[0];
        var place = first.Location is null ? "" : $" en {first.Location}";
        if (created.Count == 1)
            return NotifyAsync(squad, alerts, KeyPrefix(first.Id), resolvePrevious: false,
                $"Te citan a entrenar: {CallUps.When(first.StartsAt)}",
                $"{first.Title} el {CallUps.When(first.StartsAt)}{place}. Si no puedes ir, avísalo desde la app.",
                Link(squad.Id, first.Id), CallUps.DayOf(first.StartsAt), now, cancellationToken);

        var last = created[^1];
        var days = created.Select(s => TimeZoneInfo.ConvertTime(s.StartsAt, TrainingClock.Zone).DayOfWeek).Distinct()
            .OrderBy(d => ((int)d + 6) % 7).Select(d => Spanish.DateTimeFormat.GetDayName(d)).ToList();
        var dayList = days.Count == 1 ? days[0] : $"{string.Join(", ", days.Take(days.Count - 1))} y {days[^1]}";
        var hour = TimeZoneInfo.ConvertTime(first.StartsAt, TrainingClock.Zone).ToString("HH:mm");
        var range = $"del {TimeZoneInfo.ConvertTime(first.StartsAt, TrainingClock.Zone):dd/MM} al {TimeZoneInfo.ConvertTime(last.StartsAt, TrainingClock.Zone):dd/MM}";
        return NotifyAsync(squad, alerts, KeyPrefix(first.SeriesId ?? first.Id), resolvePrevious: false,
            $"Te citan a entrenar: {created.Count} sesiones",
            $"{first.Title}: {dayList} a las {hour}, {range}{place}. Si no puedes ir a alguna, avísalo desde la app.",
            Link(squad.Id, first.Id), CallUps.DayOf(first.StartsAt), now, cancellationToken);
    }

    public static Task MovedAsync(Squad squad, TrainingSession session, DateTimeOffset previousStart, IAlertStore alerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var place = session.Location is null ? "" : $" en {session.Location}";
        var change = previousStart == session.StartsAt
            ? $"El entrenamiento «{session.Title}» del {CallUps.When(session.StartsAt)} cambia de sitio: ahora es{place}."
            : $"El entrenamiento «{session.Title}» del {CallUps.When(previousStart)} pasa al {CallUps.When(session.StartsAt)}{place}.";
        return NotifyAsync(squad, alerts, KeyPrefix(session.Id), resolvePrevious: true,
            $"Entrenamiento cambiado: {CallUps.When(session.StartsAt)}", change,
            Link(squad.Id, session.Id), CallUps.DayOf(session.StartsAt), now, cancellationToken);
    }

    // upcoming: las sesiones anuladas que aún no han empezado, en orden. Con la serie se dan por resueltos también los
    // avisos sueltos de cada una (un cambio de hora, por ejemplo), que si no se quedarían abiertos citando a una sesión
    // que ya no existe.
    public static async Task CancelledAsync(Squad squad, IReadOnlyList<TrainingSession> upcoming, bool series, IAlertStore alerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = upcoming[0];
        var key = KeyPrefix(series && session.SeriesId is { } seriesId ? seriesId : session.Id);
        if (series)
        {
            var keys = upcoming.Select(s => KeyPrefix(s.Id) + ":").ToList();
            var players = squad.ActivePlayers.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value).ToList();
            foreach (var old in await alerts.GetOpenByKeyPrefixAsync("training-call:", players, [], null, cancellationToken))
                if (keys.Any(k => old.Key.StartsWith(k, StringComparison.Ordinal)))
                    old.Resolve(now);
        }

        var message = series
            ? $"El cuerpo técnico ha anulado los entrenamientos «{session.Title}» desde el {CallUps.When(session.StartsAt)}."
            : $"El cuerpo técnico ha anulado el entrenamiento «{session.Title}» del {CallUps.When(session.StartsAt)}.";
        await NotifyAsync(squad, alerts, key, resolvePrevious: true,
            series ? $"Entrenamientos anulados desde el {CallUps.When(session.StartsAt)}" : $"Entrenamiento anulado: {CallUps.When(session.StartsAt)}",
            message, Link(squad.Id), CallUps.DayOf(session.StartsAt), now, cancellationToken);
    }

    private static async Task NotifyAsync(
        Squad squad, IAlertStore alerts, string keyPrefix, bool resolvePrevious, string title, string message, string link, DateOnly dueOn,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var players = squad.ActivePlayers.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value).ToList();
        // Los avisos anteriores de todos los jugadores, de una vez.
        if (resolvePrevious)
            foreach (var old in await alerts.GetOpenByKeyPrefixAsync(keyPrefix, players, [], null, cancellationToken))
                old.Resolve(now);
        foreach (var playerId in players)
            alerts.Add(Alert.ForPlayer(playerId, AlertKind.TrainingCall, $"{keyPrefix}:{now.UtcTicks}", title, message, link, dueOn, now));
    }
}
