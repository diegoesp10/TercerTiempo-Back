using System.ComponentModel;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Referees;

public sealed record RefereeStatsDto(
    [property: Description("Partidos en los que figura en el equipo arbitral (de los que tiene la app).")]
    int Matches,
    [property: Description("Notas que le han puesto los equipos.")]
    int Ratings,
    [property: Description("Nota media (0-10); null sin notas.")]
    double? Average,
    [property: Description("Cuántas notas de cada valor, de 0 a 10 (11 posiciones).")]
    IReadOnlyList<int> Distribution);

public sealed record RefereeRoleStatsDto(OfficialRole Role, int Matches, int Ratings, double? Average);

public sealed record RefereeSummaryDto(
    Guid Id,
    string Name,
    [property: Description("Función que más ha ejercido.")]
    OfficialRole MainRole,
    int Matches,
    int Ratings,
    double? Average,
    DateTimeOffset? LastMatchAt);

public sealed record RefereeRatingDto(
    Guid MatchId,
    DateTimeOffset KickoffAt,
    string HomeTeamName,
    string AwayTeamName,
    [property: Description("Equipo que puso la nota.")]
    Guid TeamId,
    string TeamName,
    string? TeamCrestUrl,
    OfficialRole Role,
    int Score,
    string? Comment,
    DateTimeOffset UpdatedAt);

public sealed record RefereeMatchDto(MatchDto Match, OfficialRole Role, int Ratings, double? Average);

public sealed record RefereeDetailDto(
    Guid Id,
    string Name,
    [property: Description("Su ficha en futbolasturiano.es, si la publica.")]
    string? ProfileUrl,
    RefereeStatsDto Stats,
    IReadOnlyList<RefereeRoleStatsDto> ByRole,
    [property: Description("Las mejores notas con comentario.")]
    IReadOnlyList<RefereeRatingDto> Best,
    [property: Description("Todas sus notas, de la más reciente a la más antigua.")]
    IReadOnlyList<RefereeRatingDto> Ratings,
    IReadOnlyList<RefereeMatchDto> Matches);

public sealed record MatchOfficialDto(
    Guid RefereeId,
    string Name,
    OfficialRole Role,
    [property: Description("Nota media que le dan los equipos en todos sus partidos.")]
    double? Average,
    int Ratings,
    [property: Description("La nota de tu equipo en este partido, si ya la ha puesto.")]
    int? MyScore,
    string? MyComment);

public sealed record MatchOfficialsDto(
    Guid MatchId,
    [property: Description("Jugado y con la fecha pasada: solo entonces se puntúa.")]
    bool Played,
    [property: Description("true si la cuenta puede puntuar en nombre de uno de los dos equipos.")]
    bool CanRate,
    Guid? RaterTeamId,
    string? RaterTeamName,
    IReadOnlyList<MatchOfficialDto> Officials);

public sealed record PendingRefereeRatingDto(
    MatchDto Match,
    Guid TeamId,
    string TeamName,
    [property: Description("Miembros del equipo arbitral del partido.")]
    int Officials,
    [property: Description("Cuántos ha puntuado ya el equipo.")]
    int Rated);

public sealed record ListRefereesQuery(string? Search) : IQuery<IReadOnlyList<RefereeSummaryDto>>;

public sealed record GetRefereeQuery(Guid RefereeId) : IQuery<RefereeDetailDto>;

public sealed record GetMatchOfficialsQuery(Guid MatchId) : IQuery<MatchOfficialsDto>;

public sealed record RateRefereeCommand(
    Guid MatchId,
    Guid RefereeId,
    [property: Description("De 0 a 10.")]
    int Score,
    [property: Description("Opcional: por qué esa nota (hasta 1000 caracteres).")]
    string? Comment,
    [property: Description("Solo si el club tiene los dos equipos del partido: en nombre de cuál puntúa.")]
    Guid? TeamId = null) : ICommand<MatchOfficialsDto>;

public sealed record ListPendingRefereeRatingsQuery : IQuery<IReadOnlyList<PendingRefereeRatingDto>>;

public sealed record SyncMatchOfficialsCommand : ICommand<MatchOfficialsSynced>;

public sealed record MatchOfficialsSynced(int FromSheets, int FromProvider, int Looked);

internal sealed class RateRefereeValidator : AbstractValidator<RateRefereeCommand>
{
    public RateRefereeValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.RefereeId).NotEmpty();
        RuleFor(x => x.Score).InclusiveBetween(0, RefereeRating.MaxScore).WithName("Nota");
        RuleFor(x => x.Comment).MaximumLength(RefereeRating.MaxCommentLength).WithName("Comentario");
    }
}

// Los árbitros y sus notas son para quien prepara los partidos: los clubes y sus cuerpos técnicos, con licencia
// vigente (nunca el público ni los jugadores).
internal static class RefereeAccess
{
    public static async Task EnsureViewerAsync(ICurrentUser user, ILicenseGate licenses, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated) throw new UnauthenticatedException();
        if (user.AccountType is not (AccountType.Club or AccountType.Coach) || !await licenses.IsViewerLicensedAsync(user, cancellationToken))
            throw new ForbiddenException(ErrorCode.RefereeStaffOnly);
    }

    // El equipo del partido en cuyo nombre puntúa la cuenta: el de su club o el de la plantilla de la que es técnico vigente.
    public static async Task<Guid?> RaterTeamAsync(
        Match match, ICurrentUser user, IClubStore clubs, ISquadStore squads, TimeProvider clock, Guid? preferred, CancellationToken cancellationToken)
    {
        var candidates = new List<Guid>();
        if (user.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
        {
            candidates.AddRange(new[] { match.HomeTeamId, match.AwayTeamId }.Where(club.Manages));
        }
        else if (user.CoachId is { } coachId)
        {
            var today = SquadAccess.Today(clock);
            foreach (var teamId in new[] { match.HomeTeamId, match.AwayTeamId })
                if (await squads.GetByTeamAndSeasonAsync(teamId, match.SeasonId, cancellationToken) is { } squad && squad.IsStaff(coachId, today))
                    candidates.Add(teamId);
        }
        return preferred is { } p && candidates.Contains(p) ? p : candidates.Count > 0 ? candidates[0] : null;
    }

    public static double? Average(IEnumerable<RefereeRating> ratings)
    {
        var list = ratings.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(r => r.Score), 2);
    }
}

// Guarda el equipo arbitral de un partido venga de donde venga. Los árbitros se reconocen por el nombre normalizado
// (en el orden natural), así que la misma persona es una sola aunque cada web la escriba a su manera. La federación
// manda (authoritative); futbolasturiano.es solo rellena lo que falta y aporta el slug de su ficha.
internal sealed class MatchOfficialsWriter(IRefereeStore referees, TimeProvider clock)
{
    private readonly Dictionary<string, Referee> _known = [];

    public async Task<int> ApplyAsync(Guid matchId, IReadOnlyList<MatchOfficialData> officials, string source, bool authoritative, CancellationToken cancellationToken)
    {
        if (officials.Count == 0) return 0;
        var now = clock.GetUtcNow();
        var current = await referees.GetOfficialsAsync(matchId, cancellationToken);
        var changed = 0;
        foreach (var data in officials)
        {
            var referee = await ResolveAsync(data, now, cancellationToken);
            var existing = current.FirstOrDefault(o => o.Role == data.Role);
            if (existing is null)
            {
                referees.AddOfficial(MatchOfficial.Assign(matchId, referee.Id, data.Role, source, now));
                changed++;
            }
            else if (existing.RefereeId != referee.Id && (authoritative || existing.Source == source))
            {
                existing.Reassign(referee.Id, source, now);
                changed++;
            }
        }
        return changed;
    }

    private async Task<Referee> ResolveAsync(MatchOfficialData data, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = Referee.Normalize(data.Name);
        if (!_known.TryGetValue(key, out var referee))
        {
            referee = await referees.GetByNormalizedNameAsync(key, cancellationToken);
            if (referee is null)
            {
                referee = Referee.Create(data.Name, data.Slug, now);
                referees.Add(referee);
            }
            _known[key] = referee;
        }
        referee.LinkSlug(data.Slug);
        return referee;
    }
}

internal sealed class GetMatchOfficialsHandler(
    ICurrentUser currentUser,
    ILicenseGate licenses,
    IMatchStore matches,
    ITeamStore teams,
    IClubStore clubs,
    ISquadStore squads,
    IRefereeStore referees,
    TimeProvider clock) : IQueryHandler<GetMatchOfficialsQuery, MatchOfficialsDto>
{
    public async Task<MatchOfficialsDto> Handle(GetMatchOfficialsQuery query, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureViewerAsync(currentUser, licenses, cancellationToken);
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);
        return await MatchOfficialsBuilder.BuildAsync(match, null, currentUser, teams, clubs, squads, referees, clock, cancellationToken);
    }
}

internal static class MatchOfficialsBuilder
{
    public static async Task<MatchOfficialsDto> BuildAsync(
        Match match, Guid? preferredTeam, ICurrentUser user, ITeamStore teams, IClubStore clubs, ISquadStore squads, IRefereeStore referees,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var officials = (await referees.GetOfficialsAsync(match.Id, cancellationToken)).OrderBy(o => o.Role).ToList();
        var people = (await referees.GetByIdsAsync(officials.Select(o => o.RefereeId), cancellationToken)).ToDictionary(r => r.Id);
        var played = match.IsOver(clock.GetUtcNow());
        var raterTeam = await RefereeAccess.RaterTeamAsync(match, user, clubs, squads, clock, preferredTeam, cancellationToken);
        var mine = raterTeam is null ? [] : (await referees.GetRatingsByMatchesAsync([match.Id], cancellationToken)).Where(r => r.TeamId == raterTeam).ToList();

        var result = new List<MatchOfficialDto>();
        foreach (var official in officials)
        {
            var all = await referees.GetRatingsByRefereeAsync(official.RefereeId, cancellationToken);
            var my = mine.FirstOrDefault(r => r.RefereeId == official.RefereeId);
            result.Add(new MatchOfficialDto(
                official.RefereeId, people.GetValueOrDefault(official.RefereeId)?.Name ?? "Árbitro", official.Role,
                RefereeAccess.Average(all), all.Count, my?.Score, my?.Comment));
        }

        var teamName = raterTeam is { } id ? (await teams.GetByIdAsync(id, cancellationToken))?.Name : null;
        return new MatchOfficialsDto(match.Id, played, played && raterTeam is not null && officials.Count > 0, raterTeam, teamName, result);
    }
}

internal sealed class RateRefereeHandler(
    ICurrentUser currentUser,
    ILicenseGate licenses,
    IMatchStore matches,
    ITeamStore teams,
    IClubStore clubs,
    ISquadStore squads,
    IRefereeStore referees,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RateRefereeCommand, MatchOfficialsDto>
{
    public async Task<MatchOfficialsDto> Handle(RateRefereeCommand command, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureViewerAsync(currentUser, licenses, cancellationToken);
        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);
        var now = clock.GetUtcNow();
        if (!match.IsOver(now)) throw new ConflictException(ErrorCode.RefereeMatchNotOver);

        var teamId = await RefereeAccess.RaterTeamAsync(match, currentUser, clubs, squads, clock, command.TeamId, cancellationToken)
            ?? throw new ForbiddenException(ErrorCode.RefereeTeamNotInMatch);
        var official = (await referees.GetOfficialsAsync(match.Id, cancellationToken)).FirstOrDefault(o => o.RefereeId == command.RefereeId)
            ?? throw new NotFoundException(ErrorCode.RefereeNotInMatch);

        var (clubId, coachId) = currentUser.ClubId is { } club ? (club, (Guid?)null) : ((Guid?)null, currentUser.CoachId);
        var existing = (await referees.GetRatingsByMatchesAsync([match.Id], cancellationToken))
            .FirstOrDefault(r => r.TeamId == teamId && r.RefereeId == official.RefereeId);
        if (existing is null)
            referees.AddRating(RefereeRating.Rate(official, teamId, command.Score, command.Comment, clubId, coachId, now));
        else
            existing.Change(command.Score, command.Comment, clubId, coachId, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MatchOfficialsBuilder.BuildAsync(match, teamId, currentUser, teams, clubs, squads, referees, clock, cancellationToken);
    }
}

internal sealed class ListRefereesHandler(ICurrentUser currentUser, ILicenseGate licenses, IRefereeStore referees, IMatchStore matches)
    : IQueryHandler<ListRefereesQuery, IReadOnlyList<RefereeSummaryDto>>
{
    public async Task<IReadOnlyList<RefereeSummaryDto>> Handle(ListRefereesQuery query, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureViewerAsync(currentUser, licenses, cancellationToken);
        var all = await referees.GetAllAsync(cancellationToken);
        var officials = (await referees.GetAllOfficialsAsync(cancellationToken)).ToLookup(o => o.RefereeId);
        var ratings = (await referees.GetAllRatingsAsync(cancellationToken)).ToLookup(r => r.RefereeId);
        var kickoffs = (await matches.GetByIdsAsync(officials.SelectMany(g => g.Select(o => o.MatchId)).Distinct(), cancellationToken))
            .ToDictionary(m => m.Id, m => m.KickoffAt);

        var search = NameNormalizer.Normalize(query.Search);
        return all
            .Where(r => search.Length == 0 || r.NormalizedName.Contains(search, StringComparison.Ordinal))
            .Select(r =>
            {
                var games = officials[r.Id].ToList();
                var notes = ratings[r.Id].ToList();
                var main = games.GroupBy(o => o.Role).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault(OfficialRole.Referee);
                DateTimeOffset? last = games.Select(o => kickoffs.TryGetValue(o.MatchId, out var k) ? k : (DateTimeOffset?)null).Max();
                return new RefereeSummaryDto(r.Id, r.Name, main, games.Select(o => o.MatchId).Distinct().Count(), notes.Count, RefereeAccess.Average(notes), last);
            })
            .OrderByDescending(r => r.Average.HasValue)
            .ThenByDescending(r => r.Average)
            .ThenByDescending(r => r.Ratings)
            .ThenBy(r => r.Name)
            .ToList();
    }
}

internal sealed class GetRefereeHandler(ICurrentUser currentUser, ILicenseGate licenses, IRefereeStore referees, IMatchStore matches, ITeamStore teams)
    : IQueryHandler<GetRefereeQuery, RefereeDetailDto>
{
    private const int BestShown = 5;

    public async Task<RefereeDetailDto> Handle(GetRefereeQuery query, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureViewerAsync(currentUser, licenses, cancellationToken);
        var referee = await referees.GetByIdAsync(query.RefereeId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.RefereeNotFound, query.RefereeId);

        var officials = await referees.GetOfficialsByRefereeAsync(referee.Id, cancellationToken);
        var ratings = await referees.GetRatingsByRefereeAsync(referee.Id, cancellationToken);
        var matchMap = (await matches.GetByIdsAsync(officials.Select(o => o.MatchId).Concat(ratings.Select(r => r.MatchId)).Distinct(), cancellationToken))
            .ToDictionary(m => m.Id);
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Concat(ratings.Select(r => r.TeamId)).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);

        RefereeRatingDto? ToDto(RefereeRating r)
        {
            if (!matchMap.TryGetValue(r.MatchId, out var m)) return null;
            var team = teamMap.GetValueOrDefault(r.TeamId);
            return new RefereeRatingDto(
                m.Id, m.KickoffAt, teamMap.GetValueOrDefault(m.HomeTeamId)?.Name ?? "Local", teamMap.GetValueOrDefault(m.AwayTeamId)?.Name ?? "Visitante",
                r.TeamId, team?.Name ?? "Equipo", team?.CrestLink(), r.Role, r.Score, r.Comment, r.UpdatedAt);
        }

        var ratingDtos = ratings.OrderByDescending(r => matchMap.GetValueOrDefault(r.MatchId)?.KickoffAt).ThenByDescending(r => r.UpdatedAt)
            .Select(ToDto).OfType<RefereeRatingDto>().ToList();
        var best = ratingDtos.Where(r => r.Comment is not null).OrderByDescending(r => r.Score).ThenByDescending(r => r.KickoffAt).Take(BestShown).ToList();

        var distribution = Enumerable.Range(0, RefereeRating.MaxScore + 1).Select(score => ratings.Count(r => r.Score == score)).ToList();
        var stats = new RefereeStatsDto(officials.Select(o => o.MatchId).Distinct().Count(), ratings.Count, RefereeAccess.Average(ratings), distribution);
        var byRole = officials.GroupBy(o => o.Role).OrderBy(g => g.Key)
            .Select(g =>
            {
                var notes = ratings.Where(r => r.Role == g.Key).ToList();
                return new RefereeRoleStatsDto(g.Key, g.Count(), notes.Count, RefereeAccess.Average(notes));
            })
            .ToList();
        var games = officials
            .Where(o => matchMap.ContainsKey(o.MatchId))
            .OrderByDescending(o => matchMap[o.MatchId].KickoffAt)
            .Select(o =>
            {
                var notes = ratings.Where(r => r.MatchId == o.MatchId).ToList();
                return new RefereeMatchDto(MatchMapper.ToDto(matchMap[o.MatchId], teamMap), o.Role, notes.Count, RefereeAccess.Average(notes));
            })
            .ToList();

        var profile = referee.Slug is { } slug ? $"https://www.futbolasturiano.es/official/{slug}/" : null;
        return new RefereeDetailDto(referee.Id, referee.Name, profile, stats, byRole, best, ratingDtos, games);
    }
}

// Partidos recientes de los equipos de la cuenta con equipo arbitral, para que el panel recuerde puntuarlos.
internal sealed class ListPendingRefereeRatingsHandler(
    ICurrentUser currentUser,
    ILicenseGate licenses,
    IClubStore clubs,
    ISquadStore squads,
    IMatchStore matches,
    ITeamStore teams,
    IRefereeStore referees,
    TimeProvider clock) : IQueryHandler<ListPendingRefereeRatingsQuery, IReadOnlyList<PendingRefereeRatingDto>>
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public async Task<IReadOnlyList<PendingRefereeRatingDto>> Handle(ListPendingRefereeRatingsQuery query, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureViewerAsync(currentUser, licenses, cancellationToken);
        var now = clock.GetUtcNow();
        var myTeams = new List<Guid>();
        if (currentUser.ClubId is { } clubId && await clubs.GetByIdAsync(clubId, cancellationToken) is { } club)
            myTeams.AddRange(club.Teams.Select(t => t.TeamId));
        else if (currentUser.CoachId is { } coachId)
            myTeams.AddRange((await squads.GetByCoachAsync(coachId, SquadAccess.Today(clock), cancellationToken)).Select(s => s.TeamId));
        if (myTeams.Count == 0) return [];

        var ids = await referees.GetMatchesWithOfficialsAsync(myTeams, now - Window, cancellationToken);
        var list = (await matches.GetByIdsAsync(ids, cancellationToken)).Where(m => m.IsOver(now)).OrderByDescending(m => m.KickoffAt).ToList();
        var officials = (await referees.GetOfficialsByMatchesAsync(list.Select(m => m.Id), cancellationToken)).ToLookup(o => o.MatchId);
        var ratings = (await referees.GetRatingsByMatchesAsync(list.Select(m => m.Id), cancellationToken)).ToLookup(r => (r.MatchId, r.TeamId));
        var teamMap = (await teams.GetByIdsAsync(list.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken)).ToDictionary(t => t.Id);

        var result = new List<PendingRefereeRatingDto>();
        foreach (var match in list)
        {
            var team = myTeams.Contains(match.HomeTeamId) ? match.HomeTeamId : match.AwayTeamId;
            var people = officials[match.Id].Count();
            var rated = ratings[(match.Id, team)].Count();
            if (rated >= people) continue;
            result.Add(new PendingRefereeRatingDto(MatchMapper.ToDto(match, teamMap), team, teamMap.GetValueOrDefault(team)?.Name ?? "Equipo", people, rated));
        }
        return result;
    }
}
