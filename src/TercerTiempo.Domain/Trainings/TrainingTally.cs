using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Trainings;

// Lo justo de cada sesión y de cada línea de la lista para contar entrenamientos sin cargar sesiones enteras.
public sealed record TrainingSlot(Guid SessionId, Guid SquadId, DateTimeOffset StartsAt, int DurationMinutes, TrainingKind Kind = TrainingKind.Season, string Title = "")
{
    public bool IsDone(DateTimeOffset now) => StartsAt.AddMinutes(DurationMinutes) <= now;
}

public sealed record AttendanceMark(Guid SessionId, Guid SquadPlayerId, AttendanceStatus Status, string? Reason = null);

// Lo que fue un entrenamiento acabado para un jugador al que le tocaba: vino, faltó, faltó habiendo avisado o no pudo
// ir porque estaba lesionado (esto último no cuenta como falta).
public enum TrainingDayOutcome
{
    Attended,
    Missed,
    Warned,
    Injured,
}

public sealed record TrainingDay(TrainingSlot Slot, TrainingDayOutcome Outcome);

// Entrenamientos de un jugador en un periodo: a cuántos le tocaba ir, a cuántos vino y a cuántos faltó (y de esos, en
// cuántos avisó), con los minutos de los que hizo. Los que se perdió por estar lesionado van aparte (Injured): no le
// tocaban, así que no cuentan como faltas ni bajan su porcentaje.
public sealed record PlayerTrainingTally(int Sessions, int Attended, int Missed, int Warned, int AttendedMinutes, int Injured = 0)
{
    public static readonly PlayerTrainingTally Empty = new(0, 0, 0, 0, 0);

    public PlayerTrainingTally Add(PlayerTrainingTally other) => new(
        Sessions + other.Sessions, Attended + other.Attended, Missed + other.Missed, Warned + other.Warned, AttendedMinutes + other.AttendedMinutes,
        Injured + other.Injured);
}

public static class TrainingTally
{
    // Solo cuentan las sesiones ya acabadas de su plantilla. Le tocaba ir si era de la plantilla a esa hora; si no tiene
    // línea en la lista, contaba con venir (y vino). Una línea de quien ya se había ido solo cuenta si el cuerpo técnico
    // marcó a mano que vino o que faltó. Si estaba lesionado en esa sesión (injuredAt), no le tocaba: cuenta aparte.
    public static IReadOnlyList<TrainingDay> Days(
        SquadPlayer player, Guid squadId, IEnumerable<TrainingSlot> slots, IReadOnlyDictionary<(Guid SessionId, Guid SquadPlayerId), AttendanceStatus> marks,
        DateTimeOffset now, Func<TrainingSlot, bool>? injuredAt = null)
    {
        var days = new List<TrainingDay>();
        foreach (var slot in slots.OrderBy(s => s.StartsAt))
        {
            if (slot.SquadId != squadId || !slot.IsDone(now)) continue;

            var wasThere = TrainingSession.WasInSquadAt(player, slot.StartsAt);
            AttendanceStatus raw;
            if (marks.TryGetValue((slot.SessionId, player.Id), out var mark))
            {
                if (!wasThere && mark is not (AttendanceStatus.Attended or AttendanceStatus.Missed)) continue;
                raw = mark;
            }
            else if (wasThere)
            {
                raw = AttendanceStatus.Expected;
            }
            else
            {
                continue;
            }

            var outcome = injuredAt?.Invoke(slot) == true ? TrainingDayOutcome.Injured
                : TrainingSession.Settled(raw, true) == AttendanceStatus.Attended ? TrainingDayOutcome.Attended
                : raw == AttendanceStatus.WillMiss ? TrainingDayOutcome.Warned
                : TrainingDayOutcome.Missed;
            days.Add(new TrainingDay(slot, outcome));
        }
        return days;
    }

    public static PlayerTrainingTally Sum(IEnumerable<TrainingDay> days)
    {
        int sessions = 0, attended = 0, missed = 0, warned = 0, minutes = 0, injured = 0;
        foreach (var day in days)
        {
            switch (day.Outcome)
            {
                case TrainingDayOutcome.Injured:
                    injured++;
                    continue;
                case TrainingDayOutcome.Attended:
                    attended++;
                    minutes += day.Slot.DurationMinutes;
                    break;
                case TrainingDayOutcome.Warned:
                    missed++;
                    warned++;
                    break;
                default:
                    missed++;
                    break;
            }
            sessions++;
        }
        return new PlayerTrainingTally(sessions, attended, missed, warned, minutes, injured);
    }

    public static PlayerTrainingTally For(
        SquadPlayer player, Guid squadId, IEnumerable<TrainingSlot> slots, IReadOnlyDictionary<(Guid SessionId, Guid SquadPlayerId), AttendanceStatus> marks,
        DateTimeOffset now, Func<TrainingSlot, bool>? injuredAt = null) =>
        Sum(Days(player, squadId, slots, marks, now, injuredAt));
}
