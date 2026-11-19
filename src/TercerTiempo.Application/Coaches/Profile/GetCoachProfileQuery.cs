using System.ComponentModel;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Auth;
using TercerTiempo.Application.Coaches.Photos;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Standings;

namespace TercerTiempo.Application.Coaches.Profile;

public sealed record CoachProfileDto(
    Guid Id,
    string DisplayName,
    [property: Description("Foto que ha subido el propio técnico; null si no tiene.")]
    string? PhotoUrl,
    [property: Description("true si es el perfil de la cuenta que lo mira: solo entonces se puede cambiar la foto.")]
    bool IsMe,
    [property: Description("Historial: equipos con los que está o ha estado (entrenador, ayudante, fisio, delegado...) por los nombramientos de sus clubes, los vigentes primero y luego del más reciente al más antiguo.")]
    IReadOnlyList<CoachTeamDto> Teams,
    [property: Description("Balance de todos sus equipos mientras estuvo con ellos (cada partido una vez aunque tuviera dos funciones en el mismo equipo).")]
    CoachSummaryDto? Summary = null);

public sealed record CoachSummaryDto(
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    [property: Description("% de partidos ganados; null sin partidos.")]
    double? WinPct,
    double? DrawPct,
    double? LossPct,
    double? PointsPerMatch,
    [property: Description("Media de la posición en la clasificación de sus equipos (una por equipo y temporada); null si ninguno tiene clasificación.")]
    double? AveragePosition,
    [property: Description("Equipos y temporadas que entran en la posición media.")]
    int RankedTeams);

public sealed record CoachTeamRecordDto(int Played, int Won, int Drawn, int Lost, int GoalsFor, int GoalsAgainst, int Points);

public sealed record CoachTeamDto(
    Guid TeamId,
    string TeamName,
    string TeamShortName,
    string? TeamCrestUrl,
    string ClubName,
    string CompetitionName,
    string SeasonLabel,
    StaffRole Role,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    [property: Description("true mientras el nombramiento está vigente; false si ya caducó o lo dejó.")]
    bool Current,
    [property: Description("Plantilla de ese equipo en esa temporada (privada: solo la abren quienes tienen acceso).")]
    Guid? SquadId,
    [property: Description("Día en que dejó el equipo antes de que acabara su nombramiento (el club se lo retiró); null si lo cumplió o sigue.")]
    DateOnly? LeftOn = null,
    [property: Description("Partidos jugados por el equipo mientras estuvo con él (del inicio del nombramiento hasta que lo dejó, acabó o hoy).")]
    CoachTeamRecordDto? Record = null,
    [property: Description("Posición del equipo en la clasificación de esa competición y temporada: la actual si sigue, la última conocida si acabó; null sin clasificación.")]
    int? Position = null,
    [property: Description("Equipos de esa clasificación.")]
    int? TeamsInTable = null,
    [property: Description("Actas oficiales de esos partidos que traen el cuerpo técnico del equipo.")]
    int OfficialSheets = 0,
    [property: Description("En cuántas de ellas figura con su nombre (mismo comparador de nombres que las plantillas).")]
    int OfficialAppearances = 0,
    [property: Description("Función con la que más figura en ellas, tal como la escribe la federación («Entrenador», «2ºEntrenador»…); null si no figura.")]
    string? OfficialRole = null);

public sealed record GetCoachProfileQuery(Guid CoachId) : IQuery<CoachProfileDto>;

// Perfil de un miembro del cuerpo técnico (entrenador, ayudante, fisio, delegado...) para cualquier cuenta: su foto y su
// historial de equipos y temporadas por los nombramientos de sus clubes. Los cuerpos técnicos cambian a mitad de temporada:
// si el club le retiró el nombramiento después de que lo empezara, sale hasta ese día. Si se lo retiró antes de empezar (un
// error al nombrarlo), no estuvo nunca en el equipo y no sale.
internal sealed class GetCoachProfileHandler(
    ICurrentUser currentUser, AccountStores stores, IMatchStore matches, IStandingStore standings, IOfficialSheetStore sheets)
    : IQueryHandler<GetCoachProfileQuery, CoachProfileDto>
{
    public async Task<CoachProfileDto> Handle(GetCoachProfileQuery query, CancellationToken cancellationToken)
    {
        var coach = await stores.Coaches.GetByIdAsync(query.CoachId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.AccountNotFound, query.CoachId);
        var today = SquadAccess.Today(stores.Clock);

        var teams = new List<CoachTeamDto>();
        var teamMatches = new Dictionary<(Guid Season, Guid Team), IReadOnlyList<Match>>();
        var tables = new Dictionary<Guid, IReadOnlyList<Standing>>();
        var counted = new Dictionary<Guid, (Guid Team, Match Match)>();
        var ranked = new Dictionary<(Guid Season, Guid Team), int>();
        foreach (var appointment in await stores.Appointments.GetByCoachAsync(coach.Id, cancellationToken))
        {
            var status = appointment.StatusOn(today);
            var leftOn = LeftOn(appointment);
            if (status == AppointmentStatus.Revoked ? leftOn is null : status is not (AppointmentStatus.Active or AppointmentStatus.Expired)) continue;

            var team = await stores.Teams.GetByIdAsync(appointment.TeamId, cancellationToken);
            var club = await stores.Clubs.GetByIdAsync(appointment.ClubId, cancellationToken);
            var (competition, season) = await Resolvers.SeasonByIdAsync(stores.Competitions, appointment.SeasonId, cancellationToken);
            var squad = await stores.Squads.GetByTeamAndSeasonAsync(appointment.TeamId, appointment.SeasonId, cancellationToken);

            var key = (appointment.SeasonId, appointment.TeamId);
            if (!teamMatches.TryGetValue(key, out var seasonMatches))
                teamMatches[key] = seasonMatches = await matches.GetTeamMatchesAsync(appointment.SeasonId, appointment.TeamId, cancellationToken);
            var until = Min(leftOn ?? appointment.ValidUntil, today);
            var played = seasonMatches
                .Where(m => m.OutcomeFor(appointment.TeamId) != MatchOutcome.NotPlayed)
                .Where(m => SpainTime.Today(m.KickoffAt) is var day && day >= appointment.ValidFrom && day <= until)
                .ToList();
            foreach (var match in played) counted.TryAdd(match.Id, (appointment.TeamId, match));

            if (!tables.TryGetValue(appointment.SeasonId, out var table))
                tables[appointment.SeasonId] = table = await standings.GetBySeasonAsync(appointment.SeasonId, cancellationToken);
            var position = table.FirstOrDefault(s => s.TeamId == appointment.TeamId)?.Position;
            if (position is { } ranking) ranked[key] = ranking;
            var official = await OfficialAsync(coach.NormalizedName, appointment.TeamId, played, cancellationToken);

            teams.Add(new CoachTeamDto(
                appointment.TeamId, team?.Name ?? "Equipo", team?.ShortName ?? string.Empty, team?.CrestLink(), club?.Name ?? "Club",
                competition.Name, season.Label, appointment.Role, appointment.ValidFrom, appointment.ValidUntil,
                status == AppointmentStatus.Active, squad?.Id, leftOn,
                Record(appointment.TeamId, played),
                position,
                position is null ? null : table.Count,
                official.Sheets, official.Appearances, official.Role));
        }

        return new CoachProfileDto(
            coach.Id,
            coach.DisplayName,
            coach.PhotoLink(),
            currentUser.CoachId == coach.Id,
            teams.OrderByDescending(t => t.Current).ThenByDescending(t => t.LeftOn ?? t.ValidUntil).ThenByDescending(t => t.ValidFrom).ToList(),
            Summary(counted.Values, ranked.Values));
    }

    private static CoachTeamRecordDto Record(Guid teamId, IReadOnlyCollection<Match> played) => new(
        played.Count,
        played.Count(m => m.OutcomeFor(teamId) == MatchOutcome.Win),
        played.Count(m => m.OutcomeFor(teamId) == MatchOutcome.Draw),
        played.Count(m => m.OutcomeFor(teamId) == MatchOutcome.Loss),
        played.Sum(m => m.GoalsFor(teamId) ?? 0),
        played.Sum(m => m.GoalsAgainst(teamId) ?? 0),
        played.Sum(m => m.PointsFor(teamId) ?? 0));

    private static CoachSummaryDto Summary(IReadOnlyCollection<(Guid Team, Match Match)> played, IReadOnlyCollection<int> positions)
    {
        var won = played.Count(p => p.Match.OutcomeFor(p.Team) == MatchOutcome.Win);
        var drawn = played.Count(p => p.Match.OutcomeFor(p.Team) == MatchOutcome.Draw);
        var lost = played.Count(p => p.Match.OutcomeFor(p.Team) == MatchOutcome.Loss);
        var points = played.Sum(p => p.Match.PointsFor(p.Team) ?? 0);
        double? Pct(int value) => played.Count > 0 ? Math.Round(value * 100.0 / played.Count, 1) : null;
        return new CoachSummaryDto(
            played.Count, won, drawn, lost,
            played.Sum(p => p.Match.GoalsFor(p.Team) ?? 0),
            played.Sum(p => p.Match.GoalsAgainst(p.Team) ?? 0),
            points,
            Pct(won), Pct(drawn), Pct(lost),
            played.Count > 0 ? Math.Round(points / (double)played.Count, 2) : null,
            positions.Count > 0 ? Math.Round(positions.Average(), 1) : null,
            positions.Count);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    // El cuerpo técnico del equipo en las actas oficiales de esos partidos: en cuántas figura con su nombre y con qué
    // función (la que más se repite).
    private async Task<(int Sheets, int Appearances, string? Role)> OfficialAsync(
        string coachName, Guid teamId, IReadOnlyList<Match> played, CancellationToken cancellationToken)
    {
        if (played.Count == 0) return (0, 0, null);

        var byMatch = played.ToDictionary(m => m.Id);
        var roles = new List<string>();
        var sheetsWithStaff = 0;
        foreach (var sheet in await sheets.GetStaffByMatchIdsAsync(byMatch.Keys, cancellationToken))
        {
            var side = byMatch[sheet.MatchId].IsHome(teamId) ? MatchSide.Home : MatchSide.Away;
            var staff = sheet.StaffOf(side).ToList();
            if (staff.Count == 0) continue;
            sheetsWithStaff++;
            var mine = staff.Where(s => PersonName.SameAsSheet(s.FullName, coachName)).OrderBy(s => s.Role).FirstOrDefault();
            if (mine is not null) roles.Add(mine.RoleLabel);
        }

        var role = roles.GroupBy(r => r).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        return (sheetsWithStaff, roles.Count, role);
    }

    // Se lo retiró el club después de haberlo empezado (canjeado y ya en su periodo) y antes de que acabara.
    private static DateOnly? LeftOn(CoachAppointment appointment)
    {
        if (appointment.RevokedAt is not { } revokedAt || appointment.RedeemedAt is not { } redeemedAt) return null;
        var revokedOn = SpainTime.Today(revokedAt);
        var startedOn = SpainTime.Today(redeemedAt) > appointment.ValidFrom ? SpainTime.Today(redeemedAt) : appointment.ValidFrom;
        return revokedOn > startedOn && revokedOn <= appointment.ValidUntil ? revokedOn : null;
    }
}
