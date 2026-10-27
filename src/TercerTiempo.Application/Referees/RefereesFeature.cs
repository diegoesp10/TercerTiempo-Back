using System.ComponentModel;
using System.Text;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Teams;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Competitions;
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

public sealed record RefereeCategoryDto(
    [property: Description("Clave de la categoría para filtrar los árbitros (?category=), sacada de su nombre: «3a-asturfutbol».")]
    string Key,
    [property: Description("Nombre de la categoría; sus grupos van juntos.")]
    string Name,
    CompetitionLevel Level,
    int Tier,
    [property: Description("Árbitros que han dirigido algún partido de la categoría.")]
    int Referees,
    [property: Description("Asistentes y cuartos árbitros que han estado en algún partido de la categoría.")]
    int Assistants,
    [property: Description("Partidos de la categoría con equipo arbitral.")]
    int Matches,
    [property: Description("Notas puestas en partidos de la categoría.")]
    int Ratings,
    [property: Description("Ligas (cada grupo) de la categoría, para reconocer la de un equipo por su competitionId.")]
    IReadOnlyList<Guid> CompetitionIds);

// Por qué se ordena el listado de una categoría.
public enum RefereeSort
{
    Average = 1,
    Ratings = 2,
    Matches = 3,
    Name = 4,
    LastMatch = 5,
}

public enum SortOrder
{
    Desc = 1,
    Asc = 2,
}

public sealed record RefereeCategoryListDto(
    RefereeCategoryDto Category,
    [property: Description("Función filtrada (Main o Assistant); null si van las dos.")]
    OfficialDuty? Duty,
    RefereeSort Sort,
    SortOrder Order,
    [property: Description("Árbitros de la categoría: una fila por árbitro y función, en el orden pedido.")]
    IReadOnlyList<RefereeSummaryDto> Referees);

public sealed record RefereeSummaryDto(
    Guid Id,
    string Name,
    [property: Description("Categoría de estas cifras: un árbitro sale una vez por cada categoría y función en que ha estado.")]
    string CategoryKey,
    string Category,
    [property: Description("Main: como árbitro principal; Assistant: como asistente o cuarto árbitro.")]
    OfficialDuty Duty,
    int Matches,
    int Ratings,
    double? Average,
    DateTimeOffset? LastMatchAt);

public sealed record RefereeCategoryStatsDto(string CategoryKey, string Category, OfficialDuty Duty, int Matches, int Ratings, double? Average);

public sealed record RefereeRaterDto(
    [property: Description("Nombre del club o del técnico que puso la nota, tal como era al votar.")]
    string Name,
    [property: Description("true si la puso el club; false si la puso un técnico de su cuerpo técnico.")]
    bool Club,
    [property: Description("Función del técnico en la plantilla; null si la puso el club.")]
    StaffRole? StaffRole);

public sealed record TeamRatingDto(
    [property: Description("Equipo que puso la nota.")]
    Guid TeamId,
    string TeamName,
    string? TeamCrestUrl,
    int Score,
    [property: Description("Por qué esa nota, si lo explicó.")]
    string? Comment,
    RefereeRaterDto RatedBy,
    DateTimeOffset RatedAt);

public sealed record RefereeRatingDto(
    Guid MatchId,
    DateTimeOffset KickoffAt,
    string HomeTeamName,
    string AwayTeamName,
    [property: Description("Categoría del partido.")]
    string? CategoryKey,
    string? Category,
    [property: Description("Equipo que puso la nota.")]
    Guid TeamId,
    string TeamName,
    string? TeamCrestUrl,
    OfficialRole Role,
    int Score,
    string? Comment,
    RefereeRaterDto RatedBy,
    DateTimeOffset RatedAt);

public sealed record RefereeMatchDto(
    MatchDto Match,
    string? CategoryKey,
    string? Category,
    OfficialRole Role,
    int Ratings,
    double? Average,
    [property: Description("Las notas de los dos equipos en ese partido (primero la del local), con quién las puso y por qué.")]
    IReadOnlyList<TeamRatingDto> TeamRatings);

public sealed record RefereeDetailDto(
    Guid Id,
    string Name,
    [property: Description("Su ficha en futbolasturiano.es, si la publica.")]
    string? ProfileUrl,
    RefereeStatsDto Stats,
    [property: Description("Sus cifras en cada categoría como principal y como asistente, de la categoría más alta a la más baja.")]
    IReadOnlyList<RefereeCategoryStatsDto> ByCategory,
    [property: Description("Las mejores notas con comentario.")]
    IReadOnlyList<RefereeRatingDto> Best,
    [property: Description("Todas sus notas, de la más reciente a la más antigua.")]
    IReadOnlyList<RefereeRatingDto> Ratings,
    IReadOnlyList<RefereeMatchDto> Matches);

public sealed record MatchOfficialDto(
    Guid RefereeId,
    string Name,
    OfficialRole Role,
    [property: Description("Nota media que le dan los equipos en esa función (principal o asistente), en todos sus partidos.")]
    double? Average,
    int Ratings,
    [property: Description("Las notas de los dos equipos en este partido (primero la del local), con quién las puso y por qué.")]
    IReadOnlyList<TeamRatingDto> TeamRatings,
    [property: Description("La nota de tu equipo en este partido, si ya la ha puesto: es definitiva.")]
    int? MyScore);

public sealed record MatchOfficialsDto(
    Guid MatchId,
    TeamRefDto HomeTeam,
    TeamRefDto AwayTeam,
    [property: Description("Categoría del partido, para ver sus árbitros.")]
    string? CategoryKey,
    string? Category,
    [property: Description("Jugado y con la fecha pasada: solo entonces se puntúa.")]
    bool Played,
    [property: Description("true si la cuenta puede puntuar ahora en nombre de uno de los dos equipos: su club o su cuerpo técnico vigente, con licencia.")]
    bool CanRate,
    [property: Description("true si la cuenta es de uno de los dos equipos pero no tiene licencia vigente para puntuar.")]
    bool NeedsLicense,
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

// Los árbitros se buscan siempre dentro de una categoría: la categoría manda en el listado.
public sealed record ListRefereesQuery(
    string Category, OfficialDuty? Duty = null, string? Search = null, RefereeSort? Sort = null, SortOrder? Order = null)
    : IQuery<RefereeCategoryListDto>;

public sealed record ListRefereeCategoriesQuery : IQuery<IReadOnlyList<RefereeCategoryDto>>;

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
    Guid? TeamId = null,
    [property: Description("Tiene que ser true: la nota es definitiva y se confirma antes de guardarla.")]
    bool Confirmed = false) : ICommand<MatchOfficialsDto>;

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
        RuleFor(x => x.Confirmed).Equal(true).WithCatalogMessage(ErrorCode.RefereeRatingNotConfirmed);
    }
}

// Los árbitros y sus notas los ve cualquiera, con cuenta o sin ella; puntuar es de los clubes y sus cuerpos técnicos
// con licencia vigente.
internal static class RefereeAccess
{
    public static async Task EnsureRaterAsync(ICurrentUser user, ILicenseGate licenses, CancellationToken cancellationToken)
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

    // Quién vota, tal como es ahora: el club con su nombre o el técnico con su nombre y su función en la plantilla del equipo.
    public static async Task<RefereeRater> RaterAsync(
        Match match, Guid teamId, ICurrentUser user, IClubStore clubs, ICoachStore coaches, ISquadStore squads, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (user.ClubId is { } clubId)
            return new RefereeRater(clubId, null, user.UserId, (await clubs.GetByIdAsync(clubId, cancellationToken))?.Name ?? "Club");

        var coachId = user.CoachId ?? throw new ForbiddenException(ErrorCode.RefereeStaffOnly);
        var coach = await coaches.GetByIdAsync(coachId, cancellationToken);
        var squad = await squads.GetByTeamAndSeasonAsync(teamId, match.SeasonId, cancellationToken);
        return new RefereeRater(null, coachId, user.UserId, coach?.DisplayName ?? "Técnico", squad?.StaffMemberOf(coachId, SquadAccess.Today(clock))?.Role);
    }

    public static RefereeRaterDto Rater(RefereeRating rating) => new(rating.RaterName, rating.RatedByClubId is not null, rating.RaterStaffRole);

    public static TeamRatingDto TeamRating(RefereeRating rating, IReadOnlyDictionary<Guid, Team> teams)
    {
        var team = teams.GetValueOrDefault(rating.TeamId);
        return new TeamRatingDto(rating.TeamId, team?.Name ?? "Equipo", team?.CrestLink(), rating.Score, rating.Comment, Rater(rating), rating.CreatedAt);
    }

    // Las notas de un partido, primero la del equipo local.
    public static IReadOnlyList<TeamRatingDto> TeamRatings(IEnumerable<RefereeRating> ratings, Match match, IReadOnlyDictionary<Guid, Team> teams) =>
        ratings.OrderBy(r => r.TeamId == match.HomeTeamId ? 0 : 1).Select(r => TeamRating(r, teams)).ToList();

    public static double? Average(IEnumerable<RefereeRating> ratings)
    {
        var list = ratings.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(r => r.Score), 2);
    }
}

// La categoría de la liga de un partido, con sus grupos juntos: un árbitro de «3ª AsturFútbol» pita en cualquiera de ellos.
internal sealed record RefereeCategory(string Key, string Name, CompetitionLevel Level, int Tier);

internal static class RefereeCategories
{
    // «3ª AsturFútbol» → «3a-asturfutbol»: legible en la URL y la misma mientras no cambie el nombre de la categoría.
    public static string Key(string category)
    {
        var builder = new StringBuilder();
        foreach (var ch in NameNormalizer.Normalize(category.Replace('ª', 'a').Replace('º', 'o')))
        {
            if (char.IsAsciiLetterOrDigit(ch)) builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        return builder.ToString().TrimEnd('-');
    }

    public static async Task<Dictionary<Guid, RefereeCategory>> BySeasonAsync(ICompetitionStore competitions, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, RefereeCategory>();
        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            var category = new RefereeCategory(Key(competition.Category), competition.Category, competition.Level, competition.Tier);
            foreach (var season in competition.Seasons) map[season.Id] = category;
        }
        return map;
    }

    public static IOrderedEnumerable<T> Ordered<T>(IEnumerable<T> items, Func<T, RefereeCategory> category) =>
        items.OrderBy(x => category(x).Level).ThenBy(x => category(x).Tier).ThenBy(x => category(x).Name, StringComparer.Ordinal);

    public static RefereeCategoryDto ToDto(RefereeCategory category, IReadOnlyList<Appearance> items, IReadOnlyList<Guid> competitionIds)
    {
        int People(OfficialDuty duty) => items.Where(a => a.Duty == duty).Select(a => a.Official.RefereeId).Distinct().Count();
        return new RefereeCategoryDto(
            category.Key, category.Name, category.Level, category.Tier, People(OfficialDuty.Main), People(OfficialDuty.Assistant),
            items.Select(a => a.Official.MatchId).Distinct().Count(), items.Sum(a => a.Ratings.Count), competitionIds);
    }

    public static async Task<Dictionary<string, List<Guid>>> CompetitionIdsAsync(ICompetitionStore competitions, CancellationToken cancellationToken) =>
        (await competitions.GetAllAsync(cancellationToken)).GroupBy(c => Key(c.Category)).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());
}

// Cada vez que un árbitro ha estado en un partido, con la categoría del partido y las notas que le pusieron en él: la base
// de las cifras por categoría y función.
internal sealed record Appearance(MatchOfficial Official, Match? Match, RefereeCategory? Category, IReadOnlyList<RefereeRating> Ratings)
{
    public OfficialDuty Duty => Official.Role.Duty();
}

internal static class Appearances
{
    public static async Task<IReadOnlyList<Appearance>> LoadAsync(
        IReadOnlyList<MatchOfficial> officials, IReadOnlyList<RefereeRating> ratings, IMatchStore matches, ICompetitionStore competitions,
        CancellationToken cancellationToken)
    {
        var matchMap = (await matches.GetByIdsAsync(officials.Select(o => o.MatchId).Distinct(), cancellationToken)).ToDictionary(m => m.Id);
        var categories = await RefereeCategories.BySeasonAsync(competitions, cancellationToken);
        var notes = ratings.ToLookup(r => (r.MatchId, r.RefereeId));
        return officials
            .Select(o =>
            {
                var match = matchMap.GetValueOrDefault(o.MatchId);
                var category = match is null ? null : categories.GetValueOrDefault(match.SeasonId);
                return new Appearance(o, match, category, notes[(o.MatchId, o.RefereeId)].ToList());
            })
            .ToList();
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
    ICompetitionStore competitions,
    TimeProvider clock) : IQueryHandler<GetMatchOfficialsQuery, MatchOfficialsDto>
{
    public async Task<MatchOfficialsDto> Handle(GetMatchOfficialsQuery query, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(query.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, query.MatchId);
        return await MatchOfficialsBuilder.BuildAsync(match, null, currentUser, licenses, teams, clubs, squads, referees, competitions, clock, cancellationToken);
    }
}

internal static class MatchOfficialsBuilder
{
    public static async Task<MatchOfficialsDto> BuildAsync(
        Match match, Guid? preferredTeam, ICurrentUser user, ILicenseGate licenses, ITeamStore teams, IClubStore clubs, ISquadStore squads,
        IRefereeStore referees, ICompetitionStore competitions, TimeProvider clock, CancellationToken cancellationToken)
    {
        var officials = (await referees.GetOfficialsAsync(match.Id, cancellationToken)).OrderBy(o => o.Role).ToList();
        var people = (await referees.GetByIdsAsync(officials.Select(o => o.RefereeId), cancellationToken)).ToDictionary(r => r.Id);
        var played = match.IsOver(clock.GetUtcNow());
        var raterTeam = await RefereeAccess.RaterTeamAsync(match, user, clubs, squads, clock, preferredTeam, cancellationToken);
        var licensed = raterTeam is not null && await licenses.IsViewerLicensedAsync(user, cancellationToken);
        var here = await referees.GetRatingsByMatchesAsync([match.Id], cancellationToken);
        var teamMap = (await teams.GetByIdsAsync([match.HomeTeamId, match.AwayTeamId], cancellationToken)).ToDictionary(t => t.Id);

        var result = new List<MatchOfficialDto>();
        foreach (var official in officials)
        {
            var all = (await referees.GetRatingsByRefereeAsync(official.RefereeId, cancellationToken)).Where(r => r.Role.Duty() == official.Role.Duty()).ToList();
            var mine = here.Where(r => r.RefereeId == official.RefereeId).ToList();
            result.Add(new MatchOfficialDto(
                official.RefereeId, people.GetValueOrDefault(official.RefereeId)?.Name ?? "Árbitro", official.Role,
                RefereeAccess.Average(all), all.Count, RefereeAccess.TeamRatings(mine, match, teamMap),
                raterTeam is null ? null : mine.FirstOrDefault(r => r.TeamId == raterTeam)?.Score));
        }

        var teamName = raterTeam is { } id ? teamMap.GetValueOrDefault(id)?.Name : null;
        var competition = await competitions.GetBySeasonAsync(match.SeasonId, cancellationToken);
        return new MatchOfficialsDto(
            match.Id, MatchMapper.Ref(match.HomeTeamId, teamMap), MatchMapper.Ref(match.AwayTeamId, teamMap), competition is null ? null : RefereeCategories.Key(competition.Category), competition?.Category, played,
            played && licensed && officials.Count > 0, raterTeam is not null && !licensed, raterTeam, teamName, result);
    }
}

internal sealed class RateRefereeHandler(
    ICurrentUser currentUser,
    ILicenseGate licenses,
    IMatchStore matches,
    ITeamStore teams,
    IClubStore clubs,
    ISquadStore squads,
    ICoachStore coaches,
    IRefereeStore referees,
    ICompetitionStore competitions,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RateRefereeCommand, MatchOfficialsDto>
{
    public async Task<MatchOfficialsDto> Handle(RateRefereeCommand command, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureRaterAsync(currentUser, licenses, cancellationToken);
        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);
        var now = clock.GetUtcNow();
        if (!match.IsOver(now)) throw new ConflictException(ErrorCode.RefereeMatchNotOver);

        var teamId = await RefereeAccess.RaterTeamAsync(match, currentUser, clubs, squads, clock, command.TeamId, cancellationToken)
            ?? throw new ForbiddenException(ErrorCode.RefereeTeamNotInMatch);
        var official = (await referees.GetOfficialsAsync(match.Id, cancellationToken)).FirstOrDefault(o => o.RefereeId == command.RefereeId)
            ?? throw new NotFoundException(ErrorCode.RefereeNotInMatch);

        // Las notas son definitivas: si el equipo ya puntuó a este árbitro (su club o cualquiera de sus técnicos), no hay otra.
        var rated = (await referees.GetRatingsByMatchesAsync([match.Id], cancellationToken))
            .Any(r => r.TeamId == teamId && r.RefereeId == official.RefereeId);
        if (rated)
            throw new ConflictException(ErrorCode.RefereeAlreadyRated, (await referees.GetByIdAsync(official.RefereeId, cancellationToken))?.Name ?? "este árbitro");

        var rater = await RefereeAccess.RaterAsync(match, teamId, currentUser, clubs, coaches, squads, clock, cancellationToken);
        referees.AddRating(RefereeRating.Rate(official, teamId, command.Score, command.Comment, rater, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MatchOfficialsBuilder.BuildAsync(match, teamId, currentUser, licenses, teams, clubs, squads, referees, competitions, clock, cancellationToken);
    }
}

internal sealed class ListRefereesHandler(IRefereeStore referees, IMatchStore matches, ICompetitionStore competitions)
    : IQueryHandler<ListRefereesQuery, RefereeCategoryListDto>
{
    public async Task<RefereeCategoryListDto> Handle(ListRefereesQuery query, CancellationToken cancellationToken)
    {
        var key = RefereeCategories.Key(query.Category ?? string.Empty);
        var search = NameNormalizer.Normalize(query.Search);
        var people = (await referees.GetAllAsync(cancellationToken)).ToDictionary(r => r.Id);
        var inCategory = (await Appearances.LoadAsync(
                await referees.GetAllOfficialsAsync(cancellationToken), await referees.GetAllRatingsAsync(cancellationToken), matches, competitions, cancellationToken))
            .Where(a => a.Category?.Key == key)
            .ToList();
        if (inCategory.Count == 0) throw new NotFoundException(ErrorCode.RefereeCategoryNotFound, query.Category ?? string.Empty);

        var category = RefereeCategories.ToDto(
            inCategory[0].Category!, inCategory, (await RefereeCategories.CompetitionIdsAsync(competitions, cancellationToken)).GetValueOrDefault(key) ?? []);
        var rows = inCategory
            .Where(a => (query.Duty is null || a.Duty == query.Duty) && people.ContainsKey(a.Official.RefereeId))
            .Where(a => search.Length == 0 || people[a.Official.RefereeId].NormalizedName.Contains(search, StringComparison.Ordinal))
            .GroupBy(a => (a.Official.RefereeId, a.Duty))
            .Select(g =>
            {
                var notes = g.SelectMany(a => a.Ratings).ToList();
                return new RefereeSummaryDto(
                    g.Key.RefereeId, people[g.Key.RefereeId].Name, category.Key, category.Name, g.Key.Duty,
                    g.Select(a => a.Official.MatchId).Distinct().Count(), notes.Count, RefereeAccess.Average(notes), g.Max(a => a.Match?.KickoffAt));
            });

        var sort = query.Sort ?? RefereeSort.Average;
        var order = query.Order ?? (sort == RefereeSort.Name ? SortOrder.Asc : SortOrder.Desc);
        return new RefereeCategoryListDto(category, query.Duty, sort, order, RefereeSorting.Apply(rows, sort, order));
    }
}

// Orden del listado de una categoría. Por nota, los que aún no tienen ninguna van siempre al final; los empates se
// deshacen por notas, partidos y nombre.
internal static class RefereeSorting
{
    private static readonly StringComparer Names = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);

    public static IReadOnlyList<RefereeSummaryDto> Apply(IEnumerable<RefereeSummaryDto> rows, RefereeSort sort, SortOrder order)
    {
        var desc = order == SortOrder.Desc;
        IOrderedEnumerable<RefereeSummaryDto> sorted = sort switch
        {
            RefereeSort.Ratings => By(rows, r => r.Ratings, desc).ThenByDescending(r => r.Average ?? -1),
            RefereeSort.Matches => By(rows, r => r.Matches, desc).ThenByDescending(r => r.Average ?? -1),
            RefereeSort.Name => desc ? rows.OrderByDescending(r => r.Name, Names) : rows.OrderBy(r => r.Name, Names),
            RefereeSort.LastMatch => By(rows.OrderByDescending(r => r.LastMatchAt.HasValue), r => r.LastMatchAt, desc),
            _ => By(rows.OrderByDescending(r => r.Average.HasValue), r => r.Average, desc),
        };
        return sorted.ThenByDescending(r => r.Ratings).ThenByDescending(r => r.Matches).ThenBy(r => r.Name, Names).ToList();
    }

    private static IOrderedEnumerable<T> By<T, TKey>(IEnumerable<T> items, Func<T, TKey> key, bool desc) =>
        desc ? items.OrderByDescending(key) : items.OrderBy(key);

    private static IOrderedEnumerable<T> By<T, TKey>(IOrderedEnumerable<T> items, Func<T, TKey> key, bool desc) =>
        desc ? items.ThenByDescending(key) : items.ThenBy(key);
}

internal sealed class ListRefereeCategoriesHandler(IRefereeStore referees, IMatchStore matches, ICompetitionStore competitions)
    : IQueryHandler<ListRefereeCategoriesQuery, IReadOnlyList<RefereeCategoryDto>>
{
    public async Task<IReadOnlyList<RefereeCategoryDto>> Handle(ListRefereeCategoriesQuery query, CancellationToken cancellationToken)
    {
        var appearances = await Appearances.LoadAsync(
            await referees.GetAllOfficialsAsync(cancellationToken), await referees.GetAllRatingsAsync(cancellationToken), matches, competitions, cancellationToken);
        var competitionIds = await RefereeCategories.CompetitionIdsAsync(competitions, cancellationToken);

        var groups = appearances.Where(a => a.Category is not null).GroupBy(a => a.Category!.Key).Select(g => (Category: g.First().Category!, Items: g.ToList()));
        return RefereeCategories.Ordered(groups, g => g.Category)
            .Select(g => RefereeCategories.ToDto(g.Category, g.Items, competitionIds.GetValueOrDefault(g.Category.Key) ?? []))
            .ToList();
    }
}

internal sealed class GetRefereeHandler(IRefereeStore referees, IMatchStore matches, ITeamStore teams, ICompetitionStore competitions)
    : IQueryHandler<GetRefereeQuery, RefereeDetailDto>
{
    private const int BestShown = 5;

    public async Task<RefereeDetailDto> Handle(GetRefereeQuery query, CancellationToken cancellationToken)
    {
        var referee = await referees.GetByIdAsync(query.RefereeId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.RefereeNotFound, query.RefereeId);

        var officials = await referees.GetOfficialsByRefereeAsync(referee.Id, cancellationToken);
        var ratings = await referees.GetRatingsByRefereeAsync(referee.Id, cancellationToken);
        var matchMap = (await matches.GetByIdsAsync(officials.Select(o => o.MatchId).Concat(ratings.Select(r => r.MatchId)).Distinct(), cancellationToken))
            .ToDictionary(m => m.Id);
        var teamIds = matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Concat(ratings.Select(r => r.TeamId)).Distinct();
        var teamMap = (await teams.GetByIdsAsync(teamIds, cancellationToken)).ToDictionary(t => t.Id);
        var categories = await RefereeCategories.BySeasonAsync(competitions, cancellationToken);
        RefereeCategory? CategoryOf(Guid matchId) => matchMap.TryGetValue(matchId, out var m) ? categories.GetValueOrDefault(m.SeasonId) : null;

        RefereeRatingDto? ToDto(RefereeRating r)
        {
            if (!matchMap.TryGetValue(r.MatchId, out var m)) return null;
            var team = teamMap.GetValueOrDefault(r.TeamId);
            var category = CategoryOf(m.Id);
            return new RefereeRatingDto(
                m.Id, m.KickoffAt, teamMap.GetValueOrDefault(m.HomeTeamId)?.Name ?? "Local", teamMap.GetValueOrDefault(m.AwayTeamId)?.Name ?? "Visitante",
                category?.Key, category?.Name, r.TeamId, team?.Name ?? "Equipo", team?.CrestLink(), r.Role, r.Score, r.Comment, RefereeAccess.Rater(r), r.CreatedAt);
        }

        var ratingDtos = ratings.OrderByDescending(r => matchMap.GetValueOrDefault(r.MatchId)?.KickoffAt).ThenByDescending(r => r.CreatedAt)
            .Select(ToDto).OfType<RefereeRatingDto>().ToList();
        var best = ratingDtos.Where(r => r.Comment is not null).OrderByDescending(r => r.Score).ThenByDescending(r => r.KickoffAt).Take(BestShown).ToList();

        var distribution = Enumerable.Range(0, RefereeRating.MaxScore + 1).Select(score => ratings.Count(r => r.Score == score)).ToList();
        var stats = new RefereeStatsDto(officials.Select(o => o.MatchId).Distinct().Count(), ratings.Count, RefereeAccess.Average(ratings), distribution);
        var byCategory = RefereeCategories.Ordered(
                officials
                    .Select(o => (Official: o, Category: CategoryOf(o.MatchId)))
                    .Where(x => x.Category is not null)
                    .GroupBy(x => (x.Category!.Key, Duty: x.Official.Role.Duty()))
                    .Select(g => (Category: g.First().Category!, g.Key.Duty, Games: g.Select(x => x.Official.MatchId).ToHashSet())),
                g => g.Category)
            .ThenBy(g => g.Duty)
            .Select(g =>
            {
                var notes = ratings.Where(r => g.Games.Contains(r.MatchId) && r.Role.Duty() == g.Duty).ToList();
                return new RefereeCategoryStatsDto(g.Category.Key, g.Category.Name, g.Duty, g.Games.Count, notes.Count, RefereeAccess.Average(notes));
            })
            .ToList();
        var games = officials
            .Where(o => matchMap.ContainsKey(o.MatchId))
            .OrderByDescending(o => matchMap[o.MatchId].KickoffAt)
            .Select(o =>
            {
                var notes = ratings.Where(r => r.MatchId == o.MatchId).ToList();
                var category = CategoryOf(o.MatchId);
                return new RefereeMatchDto(
                    MatchMapper.ToDto(matchMap[o.MatchId], teamMap), category?.Key, category?.Name, o.Role, notes.Count, RefereeAccess.Average(notes),
                    RefereeAccess.TeamRatings(notes, matchMap[o.MatchId], teamMap));
            })
            .ToList();

        var profile = referee.Slug is { } slug ? $"https://www.futbolasturiano.es/official/{slug}/" : null;
        return new RefereeDetailDto(referee.Id, referee.Name, profile, stats, byCategory, best, ratingDtos, games);
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
        await RefereeAccess.EnsureRaterAsync(currentUser, licenses, cancellationToken);
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
