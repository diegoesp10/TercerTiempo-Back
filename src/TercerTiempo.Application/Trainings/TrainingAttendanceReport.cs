using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Players;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Trainings;

namespace TercerTiempo.Application.Trainings;

public sealed record PlayerTrainingTallyDto(
    Guid SquadPlayerId,
    string PlayerName,
    int? ShirtNumber,
    [property: Description("Sigue en la plantilla.")]
    bool Active,
    [property: Description("Entrenamientos acabados del periodo a los que le tocaba ir (era de la plantilla a esa hora).")]
    int Sessions,
    int Attended,
    int Missed,
    [property: Description("De los que faltó, en cuántos había avisado antes.")]
    int Warned,
    [property: Description("Minutos entrenados: la duración de cada sesión a la que vino.")]
    int AttendedMinutes,
    [property: Description("Porcentaje de asistencia (0-100); null si no le tocaba ninguna.")]
    int? AttendanceRate,
    [property: Description("Entrenamientos acabados del periodo que se perdió por estar lesionado: no cuentan en Sessions ni como faltas.")]
    int Injured = 0,
    [property: Description("Lesiones que tocan el periodo (la más reciente primero), con el motivo y las fechas: por qué no pudo entrenar.")]
    IReadOnlyList<InjuryBriefDto>? Injuries = null,
    [property: Description("Lo que fue de cada entrenamiento de Days, en el mismo orden: Attended (vino), Missed (faltó), Warned (faltó avisando) o Injured (lesionado, no cuenta como falta); null si no le tocaba (aún no era de la plantilla o ya se había ido).")]
    IReadOnlyList<TrainingDayOutcome?>? Outcomes = null,
    [property: Description("Motivos de sus faltas, por entrenamiento (Id de la sesión → motivo), cuando se apuntó alguno.")]
    IReadOnlyDictionary<Guid, string>? Reasons = null);

public sealed record TrainingAttendanceDayDto(
    Guid SessionId,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    TrainingKind Kind,
    string KindLabel,
    string Title);

public sealed record SquadTrainingAttendanceDto(
    Guid SquadId,
    DateOnly? From,
    DateOnly? To,
    [property: Description("Entrenamientos de la plantilla ya acabados en el periodo.")]
    int Sessions,
    [property: Description("Minutos que suman esas sesiones.")]
    int Minutes,
    IReadOnlyList<PlayerTrainingTallyDto> Players,
    [property: Description("Esos entrenamientos acabados, del más antiguo al más reciente: las columnas de la vista por día y por semana.")]
    IReadOnlyList<TrainingAttendanceDayDto>? Days = null);

public sealed record TrainingSeasonOptionDto(Guid SquadId, Guid SeasonId, string Label, string TeamName, DateOnly StartsOn, DateOnly EndsOn);

public sealed record MyTrainingAttendanceDto(
    [property: Description("Temporadas (plantillas) en las que ha estado el jugador, de la más reciente a la más antigua.")]
    IReadOnlyList<TrainingSeasonOptionDto> Seasons,
    [property: Description("Plantilla (temporada) que se cuenta; null = todas.")]
    Guid? SquadId,
    DateOnly? From,
    DateOnly? To,
    int Sessions,
    int Attended,
    int Missed,
    int Warned,
    int AttendedMinutes,
    int? AttendanceRate,
    [property: Description("Entrenamientos que se perdió por estar lesionado (no cuentan como faltas).")]
    int Injured = 0,
    IReadOnlyList<InjuryBriefDto>? Injuries = null);

public sealed record GetSquadTrainingAttendanceQuery(Guid SquadId, DateOnly? From, DateOnly? To, Guid? SquadPlayerId) : IQuery<SquadTrainingAttendanceDto>;

public sealed record GetMyTrainingAttendanceQuery(Guid? SquadId, DateOnly? From, DateOnly? To) : IQuery<MyTrainingAttendanceDto>;

internal static class TrainingAttendanceReport
{
    // El periodo en la hora de los campos: desde el principio del primer día hasta el final del último. Sin fechas, toda la
    // plantilla (que ya es de una sola temporada); al revés, se le da la vuelta.
    public static (DateOnly? From, DateOnly? To, DateTimeOffset Start, DateTimeOffset End) Period(DateOnly? from, DateOnly? to)
    {
        if (from is { } f && to is { } t && f > t) (from, to) = (t, f);
        var zone = TrainingClock.Zone;
        var start = from is { } day
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)))
            : DateTimeOffset.MinValue;
        var end = to is { } last
            ? new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue)))
            : DateTimeOffset.MaxValue;
        return (from, to, start, end);
    }

    // Lesionado a la hora de esa sesión (en el día de los campos asturianos).
    public static Func<DateTimeOffset, bool> InjuredAt(InjuryCalendar calendar, Guid squadPlayerId) =>
        at => calendar.On(squadPlayerId, CallUps.DayOf(at)) is not null;

    public static IReadOnlyList<InjuryBriefDto> InjuriesIn(InjuryCalendar calendar, Guid squadPlayerId, DateOnly? from, DateOnly? to) =>
        calendar.Of(squadPlayerId).Where(i => i.Overlaps(from, to)).Select(InjuryView.Brief).ToList();

    public static int? Rate(PlayerTrainingTally tally) =>
        tally.Sessions == 0 ? null : (int)Math.Round(tally.Attended * 100.0 / tally.Sessions, MidpointRounding.AwayFromZero);
}

// La asistencia de la plantilla a los entrenamientos: el club y el cuerpo técnico ven a todos; un jugador solo la suya.
internal sealed class GetSquadTrainingAttendanceHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, ITrainingStore trainings, IInjuryStore injuries, TimeProvider clock)
    : IQueryHandler<GetSquadTrainingAttendanceQuery, SquadTrainingAttendanceDto>
{
    public async Task<SquadTrainingAttendanceDto> Handle(GetSquadTrainingAttendanceQuery query, CancellationToken cancellationToken)
    {
        var squad = await TrainingAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var manager = currentUser.IsAdmin || await TrainingAccess.CanEditAsync(squad, currentUser, clock, clubs, cancellationToken);

        var only = query.SquadPlayerId;
        if (!manager)
        {
            var own = TrainingAccess.ViewerEntry(squad, currentUser) ?? throw new ForbiddenException(ErrorCode.SquadPrivate);
            if (only is { } asked && asked != own) throw new ForbiddenException(ErrorCode.SquadPrivate);
            only = own;
        }
        if (only is { } wanted && squad.Players.All(p => p.Id != wanted))
            throw new NotFoundException(ErrorCode.SquadPlayerNotFound, wanted);

        var now = clock.GetUtcNow();
        var (from, to, start, end) = TrainingAttendanceReport.Period(query.From, query.To);
        var history = await trainings.GetHistoryAsync([squad.Id], start, end < now ? end : now.AddTicks(1), only is { } id ? [id] : null, cancellationToken);
        var marks = history.MarksByLine();
        var done = history.Slots.Where(s => s.IsDone(now)).OrderBy(s => s.StartsAt).ToList();
        var column = done.Select((s, i) => (s.SessionId, i)).ToDictionary(x => x.SessionId, x => x.i);
        var reasons = history.Marks
            .Where(m => !string.IsNullOrWhiteSpace(m.Reason) && m.Status is AttendanceStatus.WillMiss or AttendanceStatus.Missed)
            .GroupBy(m => (m.SessionId, m.SquadPlayerId))
            .ToDictionary(g => g.Key, g => g.First().Reason!);
        // Lesiones: las ve quien ve la asistencia (el cuerpo técnico, todas; el jugador, las suyas).
        var calendar = new InjuryCalendar(await injuries.GetBySquadAsync(squad.Id, cancellationToken));

        var players = squad.Players
            .Where(p => only is null || p.Id == only)
            .Select(p =>
            {
                var days = TrainingTally.Days(p, squad.Id, done, marks, now, TrainingAttendanceReport.InjuredAt(calendar, p.Id));
                return (Player: p, Days: days, Tally: TrainingTally.Sum(days));
            })
            .Where(x => x.Player.IsActive || x.Tally.Sessions > 0 || x.Tally.Injured > 0 || only is not null)
            .OrderBy(x => !x.Player.IsActive)
            .ThenBy(x => x.Player.FullName)
            .Select(x =>
            {
                var outcomes = new TrainingDayOutcome?[done.Count];
                foreach (var day in x.Days) outcomes[column[day.Slot.SessionId]] = day.Outcome;
                var why = x.Days
                    .Where(d => d.Outcome is TrainingDayOutcome.Missed or TrainingDayOutcome.Warned && reasons.ContainsKey((d.Slot.SessionId, x.Player.Id)))
                    .ToDictionary(d => d.Slot.SessionId, d => reasons[(d.Slot.SessionId, x.Player.Id)]);
                return new PlayerTrainingTallyDto(
                    x.Player.Id, x.Player.FullName, x.Player.ShirtNumber, x.Player.IsActive,
                    x.Tally.Sessions, x.Tally.Attended, x.Tally.Missed, x.Tally.Warned, x.Tally.AttendedMinutes, TrainingAttendanceReport.Rate(x.Tally),
                    x.Tally.Injured, TrainingAttendanceReport.InjuriesIn(calendar, x.Player.Id, from, to), outcomes, why.Count > 0 ? why : null);
            })
            .ToList();

        var columns = done
            .Select(s => new TrainingAttendanceDayDto(
                s.SessionId, s.StartsAt, s.DurationMinutes, s.Kind, TrainingCatalog.Info(s.Kind).Label,
                string.IsNullOrWhiteSpace(s.Title) ? TrainingCatalog.Info(s.Kind).Label : s.Title))
            .ToList();
        return new SquadTrainingAttendanceDto(squad.Id, from, to, done.Count, done.Sum(s => s.DurationMinutes), players, columns);
    }
}

// Los entrenamientos del jugador que mira, de una temporada o de todas: cuántos ha hecho y cuánto tiempo suman.
internal sealed class GetMyTrainingAttendanceHandler(
    ICurrentUser currentUser, IPlayerStore players, ISquadStore squads, ITeamStore teams, ICompetitionStore competitions, ITrainingStore trainings,
    IInjuryStore injuries, TimeProvider clock)
    : IQueryHandler<GetMyTrainingAttendanceQuery, MyTrainingAttendanceDto>
{
    public async Task<MyTrainingAttendanceDto> Handle(GetMyTrainingAttendanceQuery query, CancellationToken cancellationToken)
    {
        var player = await Resolvers.CurrentPlayerAsync(currentUser, players, cancellationToken);
        var mine = await squads.GetAllByPlayerAsync(player.Id, cancellationToken);
        var (from, to, start, end) = TrainingAttendanceReport.Period(query.From, query.To);
        if (mine.Count == 0)
            return new MyTrainingAttendanceDto([], null, from, to, 0, 0, 0, 0, 0, null);

        var seasons = (await competitions.GetBySeasonIdsAsync(mine.Select(s => s.SeasonId).Distinct(), cancellationToken))
            .SelectMany(c => c.Seasons.Select(season => (Competition: c, Season: season)))
            .ToDictionary(x => x.Season.Id);
        var teamNames = (await teams.GetByIdsAsync(mine.Select(s => s.TeamId).Distinct(), cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        var options = mine
            .Where(s => seasons.ContainsKey(s.SeasonId))
            .Select(s =>
            {
                var (competition, season) = seasons[s.SeasonId];
                return new TrainingSeasonOptionDto(
                    s.Id, s.SeasonId, $"{competition.Name} · {season.Label}", teamNames.GetValueOrDefault(s.TeamId, "Tu equipo"), season.StartsOn, season.EndsOn);
            })
            .OrderByDescending(o => o.StartsOn)
            .ToList();

        if (query.SquadId is { } chosen && options.All(o => o.SquadId != chosen))
            throw new NotFoundException(ErrorCode.SquadNotFound, chosen);
        var counted = mine.Where(s => query.SquadId is null || s.Id == query.SquadId).ToList();
        var entries = counted.SelectMany(s => s.Players.Where(p => p.PlayerId == player.Id).Select(p => (Squad: s, Entry: p))).ToList();

        var now = clock.GetUtcNow();
        var history = await trainings.GetHistoryAsync(
            counted.Select(s => s.Id).ToList(), start, end < now ? end : now.AddTicks(1), entries.Select(e => e.Entry.Id).ToList(), cancellationToken);
        var marks = history.MarksByLine();
        var calendars = new Dictionary<Guid, InjuryCalendar>();
        foreach (var squad in counted)
            calendars[squad.Id] = new InjuryCalendar(await injuries.GetBySquadAsync(squad.Id, cancellationToken));
        var tally = entries.Aggregate(PlayerTrainingTally.Empty, (sum, e) =>
            sum.Add(TrainingTally.For(e.Entry, e.Squad.Id, history.Slots, marks, now, TrainingAttendanceReport.InjuredAt(calendars[e.Squad.Id], e.Entry.Id))));
        var mineInjuries = entries
            .SelectMany(e => TrainingAttendanceReport.InjuriesIn(calendars[e.Squad.Id], e.Entry.Id, from, to))
            .OrderByDescending(i => i.StartedOn)
            .ToList();

        return new MyTrainingAttendanceDto(
            options, query.SquadId, from, to, tally.Sessions, tally.Attended, tally.Missed, tally.Warned, tally.AttendedMinutes, TrainingAttendanceReport.Rate(tally),
            tally.Injured, mineInjuries);
    }
}
