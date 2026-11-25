using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record SyncCallUpRemindersCommand : ICommand<CallUpRemindersSynced>;

// CallUps: convocatorias recordadas; Raised: avisos creados (uno por persona del cuerpo técnico y otro para el club).
public sealed record CallUpRemindersSynced(int CallUps, int Raised, int Resolved);

public static class CallUpReminders
{
    // Días antes del partido (por el día, en la hora de la federación) en que llega cada recordatorio, del primero al último.
    public static readonly int[] DaysBefore = [3, 1];

    public const string Prefix = "callup-due:";

    // Los de la convocatoria de una plantilla para un partido, de cualquier fecha. La clave sigue con el día del partido y
    // el plazo: si el partido cambia de día, vuelven a llegar.
    public static string KeyPrefix(Guid squadId, Guid matchId) => $"{Prefix}{squadId:N}:{matchId:N}:";

    internal static string DatedPrefix(Guid squadId, Guid matchId, DateOnly matchDay) => $"{KeyPrefix(squadId, matchId)}{matchDay:yyyyMMdd}:";

    // De una clave, lo que va antes del plazo: plantilla, partido y día.
    internal static string DatedPrefixOf(string key) => key[..(key.LastIndexOf(':') + 1)];
}

// Recordatorio en la campanita del club y del cuerpo técnico: la convocatoria del próximo partido del equipo (el mismo que
// marcan la lista de partidos, la agenda y la página del partido) sigue sin enviarse, sin hacer o en borrador. Llega
// DaysBefore días antes del partido y otra vez el día antes, una vez por plazo, y se da por resuelto al enviarla (en el
// momento, al guardarla) o al empezar el partido, cuando ya no se puede hacer. No llama a ninguna web.
internal sealed class SyncCallUpRemindersHandler(
    IMatchStore matches,
    ISquadStore squads,
    ITeamStore teams,
    IClubStore clubs,
    ICallUpStore callUps,
    IAlertStore alerts,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncCallUpRemindersCommand, CallUpRemindersSynced>
{
    public async Task<CallUpRemindersSynced> Handle(SyncCallUpRemindersCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = CallUps.DayOf(now);
        var existing = await alerts.GetAllByKeyPrefixAsync(CallUpReminders.Prefix, cancellationToken);
        var keys = existing.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        // Las convocatorias que siguen pendientes (plantilla, partido y día): lo demás que esté abierto se resuelve al final.
        var pending = new HashSet<string>(StringComparer.Ordinal);
        int reminded = 0, raised = 0;

        // Con un día de margen sobre el plazo más largo: el corte fino va por el día del partido.
        var upcoming = await matches.GetScheduledKickingOffAsync(now, now.AddDays(CallUpReminders.DaysBefore.Max() + 1), cancellationToken);
        var names = upcoming.Count == 0
            ? new Dictionary<Guid, string>()
            : (await teams.GetByIdsAsync(upcoming.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken)).ToDictionary(t => t.Id, t => t.Name);

        foreach (var season in upcoming.GroupBy(m => m.SeasonId))
        {
            var teamIds = season.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct().ToList();
            foreach (var squad in await squads.GetByTeamIdsAsync(teamIds, season.Key, cancellationToken))
            {
                // El primero del equipo en este plazo es su próximo partido: si quedase otro antes sin jugar, también
                // estaría aquí.
                if (season.Where(m => m.Involves(squad.TeamId)).MinBy(m => m.KickoffAt) is not { } match) continue;
                var matchDay = CallUps.DayOf(match.KickoffAt);
                var daysLeft = matchDay.DayNumber - today.DayNumber;
                var reached = CallUpReminders.DaysBefore.Where(d => daysLeft <= d).ToList();
                if (reached.Count == 0) continue;

                var callUp = await callUps.GetByMatchAsync(squad.Id, match.Id, cancellationToken);
                if (callUp is { IsPublished: true }) continue;

                var dated = CallUpReminders.DatedPrefix(squad.Id, match.Id, matchDay);
                pending.Add(dated);
                var key = $"{dated}{reached.Min()}";
                if (!keys.Add(key)) continue;

                // Sin cuerpo técnico ni club no hay a quién recordárselo: no cuenta (y en la pasada siguiente se vuelve a mirar).
                var sent = await RaiseAsync(squad, match, callUp is not null, names, key, matchDay, cancellationToken);
                if (sent == 0) continue;
                raised += sent;
                reminded++;
            }
        }

        var resolved = 0;
        foreach (var alert in existing.Where(a => !a.IsResolved && !pending.Contains(CallUpReminders.DatedPrefixOf(a.Key))))
        {
            alert.Resolve(now);
            resolved++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CallUpRemindersSynced(reminded, raised, resolved);
    }

    private Task<int> RaiseAsync(
        Squad squad, Match match, bool draft, IReadOnlyDictionary<Guid, string> names, string key, DateOnly matchDay, CancellationToken cancellationToken)
    {
        var fixture = $"{names.GetValueOrDefault(match.HomeTeamId, "?")} - {names.GetValueOrDefault(match.AwayTeamId, "?")}";
        var state = draft
            ? "sigue en borrador: los jugadores no la ven hasta que la envíes"
            : "está sin hacer: elige a quién llevas, pon la cita y envíala para que cada jugador la firme";
        return StaffAlerts.RaiseAsync(squad, clubs, alerts, AlertKind.CallUpDue, key,
            draft ? $"Convocatoria en borrador: {fixture}" : $"Convocatoria por hacer: {fixture}",
            $"El partido del {CallUps.When(match.KickoffAt)} es el próximo del equipo y su convocatoria {state}. Al empezar el partido se cierra.",
            $"/plantillas/{squad.Id}/partidos/{match.Id}?tab=convocatoria", matchDay, clock, cancellationToken);
    }
}
