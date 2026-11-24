using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Squads.Suspensions;

public sealed record GetSquadDisciplineQuery(Guid SquadId) : IQuery<SquadDisciplineDto>;

public sealed record SquadDisciplineDto(
    [property: Description("El próximo partido de liga del equipo, al que se refiere todo; null si no queda ninguno.")]
    Guid? NextMatchId,
    DateTimeOffset? NextMatchKickoffAt,
    int? NextMatchday,
    [property: Description("Los jugadores apercibidos (cuatro amarillas del ciclo) o sancionados para ese partido; los demás no salen.")]
    IReadOnlyList<PlayerDisciplineDto> Players,
    [property: Description("El cuerpo técnico de la plantilla que figura en las actas oficiales, con sus tarjetas de la temporada.")]
    IReadOnlyList<SquadStaffDisciplineDto> Staff,
    [property: Description("Las amarillas del ciclo con que llega cada jugador a ese partido y por qué; quien no ha visto ninguna amarilla no sale.")]
    IReadOnlyList<PlayerYellowCycleDto> Cycles);

public sealed record PlayerDisciplineDto(Guid SquadPlayerId, DisciplineDto Next);

public sealed record PlayerYellowCycleDto(
    Guid SquadPlayerId,
    [property: Description("Amarillas del ciclo de cinco antes del próximo partido (0 a 4): con 4 está apercibido.")]
    int InCycle,
    [property: Description("La jornada de cada amarilla del ciclo, en orden (una por amarilla).")]
    IReadOnlyList<int> CycleMatchdays,
    [property: Description("Jornadas en que cumplió ciclo (la 5.ª amarilla, la 10.ª…): después se empieza de cero.")]
    IReadOnlyList<int> CompletedAt,
    [property: Description("Jornadas en que le expulsaron por doble amarilla: esas dos amarillas no cuentan para el ciclo.")]
    IReadOnlyList<int> SecondYellowMatchdays);

public sealed record SquadStaffDisciplineDto(Guid CoachId, StaffDisciplineDto Discipline);

// Cómo llega cada uno al próximo partido, para marcarlo en las listas de la plantilla y en las fichas: apercibido o
// sancionado (con lo mismo que bloquea la convocatoria y el acta). Misma visibilidad que la plantilla.
internal sealed class GetSquadDisciplineHandler(
    ICurrentUser currentUser,
    ISquadStore squads,
    IClubStore clubs,
    ICoachStore coaches,
    ICoachAppointmentStore appointments,
    IMatchStore matches,
    SuspensionReader suspensions,
    StaffDisciplineReader staffDiscipline,
    TimeProvider clock) : IQueryHandler<GetSquadDisciplineQuery, SquadDisciplineDto>
{
    public async Task<SquadDisciplineDto> Handle(GetSquadDisciplineQuery query, CancellationToken cancellationToken)
    {
        var squad = await squads.GetByIdAsync(query.SquadId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.SquadNotFound, query.SquadId);
        await SquadAccess.EnsureCanViewAsync(squad, currentUser, clock, clubs, cancellationToken);

        var season = await matches.GetTeamMatchesAsync(squad.SeasonId, squad.TeamId, cancellationToken);
        var next = StaffDisciplineReader.NextMatch(season, clock.GetUtcNow());
        if (next is null) return new SquadDisciplineDto(null, null, null, [], [], []);

        var calendar = await suspensions.LoadAsync(squad, season, cancellationToken);
        var active = squad.Players.Where(p => p.LeftOn is null).ToList();
        var players = active
            .Select(p => new PlayerDisciplineDto(
                p.Id,
                new DisciplineDto(calendar.YellowsBefore(p.Id, next.Id), calendar.On(p.Id, next.Id) is { } due ? SuspensionReader.Brief(due) : null)))
            .Where(p => p.Next.Suspension is not null || p.Next.Warned)
            .ToList();
        // El mismo ciclo que da «Apercibido», con su porqué: la tabla de la temporada cuenta todas las amarillas del acta.
        var cycles = active
            .Select(p => (p.Id, Cycle: calendar.CycleBefore(p.Id, next.Id)))
            .Where(c => !c.Cycle.IsEmpty)
            .Select(c => new PlayerYellowCycleDto(c.Id, c.Cycle.InCycle, c.Cycle.CycleMatchdays, c.Cycle.CompletedAt, c.Cycle.SecondYellowMatchdays))
            .ToList();

        var staff = new List<SquadStaffDisciplineDto>();
        var today = SquadAccess.Today(clock);
        var members = squad.StaffOn(today).ToList();
        if (members.Count > 0)
        {
            var people = await staffDiscipline.LoadAsync(squad.TeamId, squad.SeasonId, season, cancellationToken);
            var profiles = (await coaches.GetByIdsAsync(members.Select(m => m.CoachId).Distinct(), cancellationToken)).ToDictionary(c => c.Id);
            var typed = (await appointments.GetByTeamAndSeasonAsync(squad.TeamId, squad.SeasonId, cancellationToken))
                .Where(a => a.CoachId is not null)
                .GroupBy(a => a.CoachId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(a => a.NormalizedCoachName).ToList());
            foreach (var coachId in members.Select(m => m.CoachId).Distinct())
            {
                var names = new List<string>();
                if (profiles.TryGetValue(coachId, out var coach)) names.Add(coach.NormalizedName);
                names.AddRange(typed.GetValueOrDefault(coachId) ?? []);
                if (people.For(names.Distinct()) is { } person)
                    staff.Add(new SquadStaffDisciplineDto(coachId, person.ToDto()));
            }
        }

        return new SquadDisciplineDto(next.Id, next.KickoffAt, next.Matchday, players, staff, cycles);
    }
}
