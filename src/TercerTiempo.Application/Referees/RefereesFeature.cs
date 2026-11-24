using System.ComponentModel;
using System.Text;
using FluentValidation;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.Competitions;
using TercerTiempo.Application.Licensing;
using TercerTiempo.Application.Matches;
using TercerTiempo.Application.Moderation;
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
    [property: Description("Notas que le han puesto los equipos (sin contar los «No votado»).")]
    int Ratings,
    [property: Description("Nota media (0-10); null sin notas.")]
    double? Average,
    [property: Description("Veces que un equipo decidió no votarle («No votado»).")]
    int NoVotes,
    [property: Description("Cuántas notas de cada valor, de 0 a 10 (11 posiciones).")]
    IReadOnlyList<int> Distribution);

public sealed record RefereeCategoryDto(
    [property: Description("Clave de la categoría para filtrar los árbitros (?category=), sacada de su nombre: «3a-asturfutbol».")]
    string Key,
    [property: Description("Nombre de la categoría; sus grupos van juntos.")]
    string Name,
    CompetitionLevel Level,
    int Tier,
    [property: Description("Ámbito de la categoría (Nacional, Asturias…), para elegirla como las ligas: primero el ámbito.")]
    TerritoryDto Territory,
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
    [property: Description("De 0 a 10; null si el equipo decidió no votar («No votado»).")]
    int? Score,
    [property: Description("true si el equipo decidió no votar: queda quién lo decidió, sin nota ni comentario.")]
    bool NoVote,
    [property: Description("Por qué esa nota, si lo explicó; null si no lo hizo o si la administración lo retiró.")]
    string? Comment,
    [property: Description("Comentario retirado por la administración por no respetar las normas (la nota sigue).")]
    bool CommentHidden,
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
    [property: Description("De 0 a 10; null si el equipo decidió no votar.")]
    int? Score,
    bool NoVote,
    [property: Description("Null si no lo hay o si la administración lo retiró.")]
    string? Comment,
    bool CommentHidden,
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
    int? MyScore,
    [property: Description("true si tu equipo decidió no votarle en este partido (también definitivo).")]
    bool MyNoVote);

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
    [property: Description("Cuántos ha contestado ya el equipo (con nota o «No votado»).")]
    int Rated,
    [property: Description("De esos, cuántos quedaron como «No votado».")]
    int NoVotes);

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
    [property: Description("De 0 a 10; null (o sin enviar) si noVote es true.")]
    int? Score,
    [property: Description("Opcional: por qué esa nota (hasta 1000 caracteres).")]
    string? Comment,
    [property: Description("Solo si el club tiene los dos equipos del partido: en nombre de cuál puntúa.")]
    Guid? TeamId = null,
    [property: Description("Tiene que ser true: la nota es definitiva y se confirma antes de guardarla.")]
    bool Confirmed = false,
    [property: Description("true para no votar a este árbitro («No votado»): sin nota ni comentario, y también definitivo.")]
    bool NoVote = false) : ICommand<MatchOfficialsDto>;

// No votar a nadie del equipo arbitral de un partido: «No votado» para cada uno al que el equipo aún no ha contestado.
public sealed record NoVoteMatchOfficialsCommand(
    Guid MatchId,
    [property: Description("Solo si el club tiene los dos equipos del partido: en nombre de cuál.")]
    Guid? TeamId = null,
    [property: Description("Tiene que ser true: es definitivo.")]
    bool Confirmed = false) : ICommand<MatchOfficialsDto>;

public sealed record ListPendingRefereeRatingsQuery : IQuery<IReadOnlyList<PendingRefereeRatingDto>>;

public sealed record SyncMatchOfficialsCommand : ICommand<MatchOfficialsSynced>;

public sealed record MatchOfficialsSynced(int FromSheets, int FromProvider, int Looked, int Designated);

internal sealed class RateRefereeValidator : AbstractValidator<RateRefereeCommand>
{
    public RateRefereeValidator(CommentModeration moderation)
    {
        RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.RefereeId).NotEmpty();
        RuleFor(x => x.Score).NotNull().InclusiveBetween(0, RefereeRating.MaxScore).WithName("Nota").When(x => !x.NoVote);
        RuleFor(x => x.Score).Null().WithName("Nota").When(x => x.NoVote);
        RuleFor(x => x.Comment).Empty().WithCatalogMessage(ErrorCode.RefereeNoVoteComment).When(x => x.NoVote);
        RuleFor(x => x.Comment).MaximumLength(RefereeRating.MaxCommentLength).WithName("Comentario");
        // Sin palabrotas, insultos, vejaciones ni amenazas (tabla PalabrasProhibidas): el comentario es público y definitivo.
        RuleFor(x => x.Comment).CustomAsync(async (comment, context, cancellationToken) =>
        {
            var found = await moderation.FindAsync(comment, cancellationToken);
            if (found.Count > 0) context.AddFailure(CommentModeration.Failure(nameof(RateRefereeCommand.Comment), found));
        });
        RuleFor(x => x.Confirmed).Equal(true).WithCatalogMessage(ErrorCode.RefereeRatingNotConfirmed);
    }
}

internal sealed class NoVoteMatchOfficialsValidator : AbstractValidator<NoVoteMatchOfficialsCommand>
{
    public NoVoteMatchOfficialsValidator()
    {
        RuleFor(x => x.MatchId).NotEmpty();
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
        return new TeamRatingDto(
            rating.TeamId, team?.Name ?? "Equipo", team?.CrestLink(), rating.Score, rating.NoVote, rating.PublicComment, rating.CommentHidden, Rater(rating), rating.CreatedAt);
    }

    // Las notas de un partido, primero la del equipo local.
    public static IReadOnlyList<TeamRatingDto> TeamRatings(IEnumerable<RefereeRating> ratings, Match match, IReadOnlyDictionary<Guid, Team> teams) =>
        ratings.OrderBy(r => r.TeamId == match.HomeTeamId ? 0 : 1).Select(r => TeamRating(r, teams)).ToList();

    // Las medias y los recuentos de notas solo cuentan las notas: un «No votado» no es un cero.
    public static IEnumerable<RefereeRating> Scored(IEnumerable<RefereeRating> ratings) => ratings.Where(r => r.Score is not null);

    public static double? Average(IEnumerable<RefereeRating> ratings)
    {
        var list = Scored(ratings).ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(r => r.Score!.Value), 2);
    }
}

// La categoría de la liga de un partido, con sus grupos juntos: un árbitro de «3ª AsturFútbol» pita en cualquiera de ellos.
internal sealed record RefereeCategory(string Key, string Name, CompetitionLevel Level, int Tier, TerritoryDto Territory);

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

    public static async Task<Dictionary<Guid, RefereeCategory>> BySeasonAsync(
        ICompetitionStore competitions, ITerritoryStore territories, CancellationToken cancellationToken)
    {
        var byId = (await territories.GetAllAsync(cancellationToken)).ToDictionary(t => t.Id);
        var map = new Dictionary<Guid, RefereeCategory>();
        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            var category = Of(competition, byId);
            foreach (var season in competition.Seasons) map[season.Id] = category;
        }
        return map;
    }

    public static RefereeCategory Of(Competition competition, IReadOnlyDictionary<Guid, Territory> territories) =>
        new(Key(competition.Category), competition.Category, competition.Level, competition.Tier, TerritoryDto.Of(competition.TerritoryId, territories));

    public static IOrderedEnumerable<T> Ordered<T>(IEnumerable<T> items, Func<T, RefereeCategory> category) =>
        items.OrderBy(x => category(x).Level).ThenBy(x => category(x).Tier).ThenBy(x => category(x).Name, StringComparer.Ordinal);

    public static RefereeCategoryDto ToDto(RefereeCategory category, IReadOnlyList<Appearance> items, IReadOnlyList<Guid> competitionIds)
    {
        int People(OfficialDuty duty) => items.Where(a => a.Duty == duty).Select(a => a.RefereeId).Distinct().Count();
        return new RefereeCategoryDto(
            category.Key, category.Name, category.Level, category.Tier, category.Territory, People(OfficialDuty.Main), People(OfficialDuty.Assistant),
            items.Select(a => a.MatchId).Distinct().Count(), items.Sum(a => a.Scored), competitionIds);
    }
}

// Cada vez que un árbitro ha estado en un partido, con la categoría del partido y las notas que le pusieron en él (cuántas
// puntuadas y su suma): la base de las cifras por categoría y función.
internal sealed record Appearance(Guid MatchId, Guid RefereeId, OfficialDuty Duty, RefereeCategory Category, DateTimeOffset? KickoffAt, int Scored, int ScoreSum);

internal sealed record RefereeName(string Name, string NormalizedName);

// Todas las designaciones con su categoría, los nombres de los árbitros y las ligas de cada categoría. Se arma con
// proyecciones mínimas (sin cargar partidos ni notas enteras) y se guarda en PublicDataCache: cambia con los datos
// reales y con cada nota, y las dos cosas invalidan lo guardado.
internal sealed record RefereeStats(
    IReadOnlyList<Appearance> Appearances, IReadOnlyDictionary<Guid, RefereeName> People, IReadOnlyDictionary<string, List<Guid>> CompetitionIds)
{
    public static Task<RefereeStats> GetAsync(
        IRefereeStore referees, ICompetitionStore competitions, ITerritoryStore territories, PublicDataCache cache, CancellationToken cancellationToken) =>
        cache.GetAsync("referee-stats", ct => LoadAsync(referees, competitions, territories, ct), cancellationToken);

    private static async Task<RefereeStats> LoadAsync(
        IRefereeStore referees, ICompetitionStore competitions, ITerritoryStore territories, CancellationToken cancellationToken)
    {
        var all = await competitions.GetAllAsync(cancellationToken);
        var byId = (await territories.GetAllAsync(cancellationToken)).ToDictionary(t => t.Id);
        var categories = new Dictionary<Guid, RefereeCategory>();
        foreach (var competition in all)
        {
            var category = RefereeCategories.Of(competition, byId);
            foreach (var season in competition.Seasons) categories[season.Id] = category;
        }

        var notes = (await referees.GetAllScoresAsync(cancellationToken))
            .GroupBy(r => (r.MatchId, r.RefereeId))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Sum: g.Sum(r => r.Score)));
        var appearances = new List<Appearance>();
        foreach (var row in await referees.GetAllAppearancesAsync(cancellationToken))
        {
            if (row.SeasonId is not { } seasonId || !categories.TryGetValue(seasonId, out var category)) continue;
            var (count, sum) = notes.GetValueOrDefault((row.MatchId, row.RefereeId));
            appearances.Add(new Appearance(row.MatchId, row.RefereeId, row.Role.Duty(), category, row.KickoffAt, count, sum));
        }

        var people = (await referees.GetAllAsync(cancellationToken)).ToDictionary(r => r.Id, r => new RefereeName(r.Name, r.NormalizedName));
        var competitionIds = all.GroupBy(c => RefereeCategories.Key(c.Category)).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());
        return new RefereeStats(appearances, people, competitionIds);
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

    // Lo que la federación designa para un partido por jugar: sustituye a lo designado antes (puede cambiar hasta el día
    // del partido) y quita las funciones que ya no publica. Devuelve cuántas funciones han cambiado.
    public async Task<int> DesignateAsync(Guid matchId, IReadOnlyList<MatchOfficialData> designated, string source, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var current = await referees.GetDesignationsAsync(matchId, cancellationToken);
        var changed = 0;
        foreach (var data in designated)
        {
            var referee = await ResolveAsync(data, now, cancellationToken);
            if (current.FirstOrDefault(d => d.Role == data.Role) is { } existing)
            {
                if (existing.Change(referee.Id, now)) changed++;
            }
            else
            {
                referees.AddDesignation(RefereeDesignation.Designate(matchId, referee.Id, data.Role, source, now));
                changed++;
            }
        }

        foreach (var withdrawn in current.Where(d => designated.All(x => x.Role != d.Role)))
        {
            referees.RemoveDesignation(withdrawn);
            changed++;
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

        // Media y número de notas de cada árbitro en su función: una consulta agrupada para todo el equipo arbitral.
        var totals = (await referees.GetScoreTotalsAsync(officials.Select(o => o.RefereeId), cancellationToken))
            .GroupBy(t => (t.RefereeId, Duty: t.Role.Duty()))
            .ToDictionary(g => g.Key, g => (Count: g.Sum(t => t.Count), Sum: g.Sum(t => t.Sum)));

        var result = new List<MatchOfficialDto>();
        foreach (var official in officials)
        {
            var (count, sum) = totals.GetValueOrDefault((official.RefereeId, official.Role.Duty()));
            var mine = here.Where(r => r.RefereeId == official.RefereeId).ToList();
            result.Add(new MatchOfficialDto(
                official.RefereeId, people.GetValueOrDefault(official.RefereeId)?.Name ?? "Árbitro", official.Role,
                count == 0 ? null : Math.Round((double)sum / count, 2), count, RefereeAccess.TeamRatings(mine, match, teamMap),
                raterTeam is null ? null : mine.FirstOrDefault(r => r.TeamId == raterTeam)?.Score,
                raterTeam is not null && mine.Any(r => r.TeamId == raterTeam && r.NoVote)));
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
        referees.AddRating(RefereeRating.Rate(official, teamId, command.NoVote ? null : command.Score, command.NoVote ? null : command.Comment, rater, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MatchOfficialsBuilder.BuildAsync(match, teamId, currentUser, licenses, teams, clubs, squads, referees, competitions, clock, cancellationToken);
    }
}

internal sealed class NoVoteMatchOfficialsHandler(
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
    TimeProvider clock) : ICommandHandler<NoVoteMatchOfficialsCommand, MatchOfficialsDto>
{
    public async Task<MatchOfficialsDto> Handle(NoVoteMatchOfficialsCommand command, CancellationToken cancellationToken)
    {
        await RefereeAccess.EnsureRaterAsync(currentUser, licenses, cancellationToken);
        var match = await matches.GetByIdAsync(command.MatchId, cancellationToken)
            ?? throw new NotFoundException(ErrorCode.MatchNotFound, command.MatchId);
        var now = clock.GetUtcNow();
        if (!match.IsOver(now)) throw new ConflictException(ErrorCode.RefereeMatchNotOver);

        var teamId = await RefereeAccess.RaterTeamAsync(match, currentUser, clubs, squads, clock, command.TeamId, cancellationToken)
            ?? throw new ForbiddenException(ErrorCode.RefereeTeamNotInMatch);
        var answered = (await referees.GetRatingsByMatchesAsync([match.Id], cancellationToken))
            .Where(r => r.TeamId == teamId).Select(r => r.RefereeId).ToHashSet();
        var open = (await referees.GetOfficialsAsync(match.Id, cancellationToken)).Where(o => !answered.Contains(o.RefereeId)).ToList();
        if (open.Count == 0) throw new ConflictException(ErrorCode.RefereeNothingToVote);

        var rater = await RefereeAccess.RaterAsync(match, teamId, currentUser, clubs, coaches, squads, clock, cancellationToken);
        foreach (var official in open)
            referees.AddRating(RefereeRating.Rate(official, teamId, null, null, rater, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MatchOfficialsBuilder.BuildAsync(match, teamId, currentUser, licenses, teams, clubs, squads, referees, competitions, clock, cancellationToken);
    }
}

internal sealed class ListRefereesHandler(IRefereeStore referees, ICompetitionStore competitions, ITerritoryStore territories, PublicDataCache cache)
    : IQueryHandler<ListRefereesQuery, RefereeCategoryListDto>
{
    public async Task<RefereeCategoryListDto> Handle(ListRefereesQuery query, CancellationToken cancellationToken)
    {
        var key = RefereeCategories.Key(query.Category ?? string.Empty);
        var search = NameNormalizer.Normalize(query.Search);
        var stats = await RefereeStats.GetAsync(referees, competitions, territories, cache, cancellationToken);
        var people = stats.People;
        var inCategory = stats.Appearances.Where(a => a.Category.Key == key).ToList();
        if (inCategory.Count == 0) throw new NotFoundException(ErrorCode.RefereeCategoryNotFound, query.Category ?? string.Empty);

        var category = RefereeCategories.ToDto(inCategory[0].Category, inCategory, stats.CompetitionIds.GetValueOrDefault(key) ?? []);
        var rows = inCategory
            .Where(a => (query.Duty is null || a.Duty == query.Duty) && people.ContainsKey(a.RefereeId))
            .Where(a => search.Length == 0 || people[a.RefereeId].NormalizedName.Contains(search, StringComparison.Ordinal))
            .GroupBy(a => (a.RefereeId, a.Duty))
            .Select(g =>
            {
                var scored = g.Sum(a => a.Scored);
                return new RefereeSummaryDto(
                    g.Key.RefereeId, people[g.Key.RefereeId].Name, category.Key, category.Name, g.Key.Duty,
                    g.Select(a => a.MatchId).Distinct().Count(), scored, scored == 0 ? null : Math.Round((double)g.Sum(a => a.ScoreSum) / scored, 2),
                    g.Max(a => a.KickoffAt));
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

internal sealed class ListRefereeCategoriesHandler(IRefereeStore referees, ICompetitionStore competitions, ITerritoryStore territories, PublicDataCache cache)
    : IQueryHandler<ListRefereeCategoriesQuery, IReadOnlyList<RefereeCategoryDto>>
{
    public async Task<IReadOnlyList<RefereeCategoryDto>> Handle(ListRefereeCategoriesQuery query, CancellationToken cancellationToken)
    {
        var stats = await RefereeStats.GetAsync(referees, competitions, territories, cache, cancellationToken);
        var groups = stats.Appearances.GroupBy(a => a.Category.Key).Select(g => (Category: g.First().Category, Items: g.ToList()));
        return RefereeCategories.Ordered(groups, g => g.Category)
            .Select(g => RefereeCategories.ToDto(g.Category, g.Items, stats.CompetitionIds.GetValueOrDefault(g.Category.Key) ?? []))
            .ToList();
    }
}

internal sealed class GetRefereeHandler(IRefereeStore referees, IMatchStore matches, ITeamStore teams, ICompetitionStore competitions, ITerritoryStore territories)
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
        var categories = await RefereeCategories.BySeasonAsync(competitions, territories, cancellationToken);
        RefereeCategory? CategoryOf(Guid matchId) => matchMap.TryGetValue(matchId, out var m) ? categories.GetValueOrDefault(m.SeasonId) : null;

        RefereeRatingDto? ToDto(RefereeRating r)
        {
            if (!matchMap.TryGetValue(r.MatchId, out var m)) return null;
            var team = teamMap.GetValueOrDefault(r.TeamId);
            var category = CategoryOf(m.Id);
            return new RefereeRatingDto(
                m.Id, m.KickoffAt, teamMap.GetValueOrDefault(m.HomeTeamId)?.Name ?? "Local", teamMap.GetValueOrDefault(m.AwayTeamId)?.Name ?? "Visitante",
                category?.Key, category?.Name, r.TeamId, team?.Name ?? "Equipo", team?.CrestLink(), r.Role, r.Score, r.NoVote, r.PublicComment, r.CommentHidden, RefereeAccess.Rater(r), r.CreatedAt);
        }

        var ratingDtos = ratings.OrderByDescending(r => matchMap.GetValueOrDefault(r.MatchId)?.KickoffAt).ThenByDescending(r => r.CreatedAt)
            .Select(ToDto).OfType<RefereeRatingDto>().ToList();
        var best = ratingDtos.Where(r => r.Comment is not null).OrderByDescending(r => r.Score).ThenByDescending(r => r.KickoffAt).Take(BestShown).ToList();

        var distribution = Enumerable.Range(0, RefereeRating.MaxScore + 1).Select(score => ratings.Count(r => r.Score == score)).ToList();
        var stats = new RefereeStatsDto(
            officials.Select(o => o.MatchId).Distinct().Count(), RefereeAccess.Scored(ratings).Count(), RefereeAccess.Average(ratings), ratings.Count(r => r.NoVote), distribution);
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
                return new RefereeCategoryStatsDto(g.Category.Key, g.Category.Name, g.Duty, g.Games.Count, RefereeAccess.Scored(notes).Count(), RefereeAccess.Average(notes));
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
                    MatchMapper.ToDto(matchMap[o.MatchId], teamMap), category?.Key, category?.Name, o.Role, RefereeAccess.Scored(notes).Count(), RefereeAccess.Average(notes),
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
            myTeams.AddRange((await squads.GetRefsByCoachAsync(coachId, SquadAccess.Today(clock), cancellationToken)).Select(s => s.TeamId));
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
            var answers = ratings[(match.Id, team)].ToList();
            if (answers.Count >= people) continue;
            result.Add(new PendingRefereeRatingDto(
                MatchMapper.ToDto(match, teamMap), team, teamMap.GetValueOrDefault(team)?.Name ?? "Equipo", people, answers.Count, answers.Count(r => r.NoVote)));
        }
        return result;
    }
}
