using System.Globalization;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Referees;

// Función de cada miembro del equipo arbitral de un partido.
public enum OfficialRole
{
    Referee = 1,
    AssistantReferee1 = 2,
    AssistantReferee2 = 3,
    FourthOfficial = 4,
}

// Lo que se compara entre árbitros: dirigir el partido (principal) o ayudar desde fuera (asistentes y cuarto árbitro).
public enum OfficialDuty
{
    Main = 1,
    Assistant = 2,
}

public static class OfficialRoles
{
    public static OfficialDuty Duty(this OfficialRole role) => role == OfficialRole.Referee ? OfficialDuty.Main : OfficialDuty.Assistant;
}

// Árbitro, asistente o cuarto árbitro. Es una entidad de referencia: sale de las actas de la federación («Apellidos,
// Nombre») y de futbolasturiano.es («Nombre Apellidos», con su slug de /official/), y se identifica por el nombre
// normalizado en el orden natural para que las dos fuentes den la misma persona.
public sealed class Referee : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    // Slug de su ficha en futbolasturiano.es, si lo publica.
    public string? Slug { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Referee() { }

    public static Referee Create(string name, string? slug, DateTimeOffset now)
    {
        var display = DisplayName(name);
        return new Referee
        {
            Id = NewId(),
            Name = Guard.NotEmpty(display, "árbitro", 150),
            NormalizedName = Normalize(display),
            Slug = CleanSlug(slug),
            CreatedAt = now,
        };
    }

    public void LinkSlug(string? slug) => Slug ??= CleanSlug(slug);

    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase) { "de", "del", "la", "las", "los", "y" };

    // «Ovies García, Sara» → «Sara Ovies García»; los nombres en mayúsculas pasan a mayúscula inicial y las partículas
    // de los apellidos van en minúscula («David del Valle Prieto»).
    public static string DisplayName(string name)
    {
        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var comma = clean.IndexOf(',');
        if (comma > 0) clean = $"{clean[(comma + 1)..].Trim()} {clean[..comma].Trim()}".Trim();
        if (!clean.Any(char.IsLower)) clean = CultureInfo.GetCultureInfo("es-ES").TextInfo.ToTitleCase(clean.ToLowerInvariant());
        var words = clean.Split(' ');
        return string.Join(' ', words.Select((word, i) => i > 0 && i < words.Length - 1 && Particles.Contains(word) ? word.ToLowerInvariant() : word));
    }

    public static string Normalize(string name) => NameNormalizer.Normalize(DisplayName(name));

    private static string? CleanSlug(string? slug) => string.IsNullOrWhiteSpace(slug) ? null : Guard.NotEmpty(slug.Trim().ToLowerInvariant(), "slug", 150);
}

// Un miembro del equipo arbitral tal como lo publica una web (nombre en su formato y, si lo hay, su slug).
public sealed record MatchOfficialData(OfficialRole Role, string Name, string? Slug = null);

// Un miembro del equipo arbitral de un partido: una fila por función. Source es la web de la que salió (la federación
// manda sobre futbolasturiano.es, que solo completa lo que falta).
public sealed class MatchOfficial : Entity
{
    public Guid MatchId { get; private set; }
    public Guid RefereeId { get; private set; }
    public OfficialRole Role { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private MatchOfficial() { }

    public static MatchOfficial Assign(Guid matchId, Guid refereeId, OfficialRole role, string source, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role)) throw new DomainException(ErrorCode.RefereeRoleInvalid);
        return new MatchOfficial
        {
            Id = NewId(),
            MatchId = Guard.NotEmpty(matchId, nameof(matchId)),
            RefereeId = Guard.NotEmpty(refereeId, nameof(refereeId)),
            Role = role,
            Source = Guard.NotEmpty(source, "fuente", 40),
            UpdatedAt = now,
        };
    }

    public void Reassign(Guid refereeId, string source, DateTimeOffset now)
    {
        RefereeId = Guard.NotEmpty(refereeId, nameof(refereeId));
        Source = Guard.NotEmpty(source, "fuente", 40);
        UpdatedAt = now;
    }
}

// Cuándo se buscó en futbolasturiano.es el equipo arbitral de un partido sin él, para no pedir su página cada hora.
public sealed class MatchOfficialLookup
{
    public Guid MatchId { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }
    public int Attempts { get; private set; }

    private MatchOfficialLookup() { }

    public static MatchOfficialLookup First(Guid matchId, DateTimeOffset now) => new() { MatchId = matchId, CheckedAt = now, Attempts = 1 };

    public void Again(DateTimeOffset now)
    {
        CheckedAt = now;
        Attempts++;
    }
}

// Quién pone una nota: el club del equipo (su nombre) o un técnico de su plantilla (su nombre y su función en ella), y
// con qué cuenta. Se guarda tal cual estaba al votar.
public sealed record RefereeRater(Guid? ClubId, Guid? CoachId, Guid? UserId, string Name, StaffRole? StaffRole = null);

// Nota de un equipo a un miembro del equipo arbitral en un partido que ha jugado: de 0 a 10, con un comentario opcional
// que la explique. La pone el club del equipo o su cuerpo técnico vigente; una por equipo, árbitro y partido, y es
// definitiva: no se cambia ni se borra. Es pública, con quién la puso, para que se vea el porqué de cada nota.
public sealed class RefereeRating : AggregateRoot
{
    public const int MaxScore = 10;
    public const int MaxCommentLength = 1000;
    public const int MaxRaterNameLength = 150;

    public Guid MatchId { get; private set; }
    public Guid RefereeId { get; private set; }
    public OfficialRole Role { get; private set; }
    public Guid TeamId { get; private set; }
    public int Score { get; private set; }
    public string? Comment { get; private set; }
    public Guid? RatedByClubId { get; private set; }
    public Guid? RatedByCoachId { get; private set; }
    public Guid? RatedByUserId { get; private set; }
    public string RaterName { get; private set; } = string.Empty;
    public StaffRole? RaterStaffRole { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RefereeRating() { }

    public static RefereeRating Rate(MatchOfficial official, Guid teamId, int score, string? comment, RefereeRater rater, DateTimeOffset now)
    {
        if (score is < 0 or > MaxScore) throw new DomainException(ErrorCode.RefereeScoreRange, MaxScore);
        if (rater.ClubId is null == rater.CoachId is null) throw new DomainException(ErrorCode.RefereeRaterRequired);

        return new RefereeRating
        {
            Id = NewId(),
            MatchId = official.MatchId,
            RefereeId = official.RefereeId,
            Role = official.Role,
            TeamId = Guard.NotEmpty(teamId, nameof(teamId)),
            Score = score,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.NotEmpty(comment, "comentario", MaxCommentLength),
            RatedByClubId = rater.ClubId,
            RatedByCoachId = rater.CoachId,
            RatedByUserId = rater.UserId,
            RaterName = Guard.NotEmpty(rater.Name, "quién puntúa", MaxRaterNameLength),
            RaterStaffRole = rater.CoachId is null ? null : rater.StaffRole,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

// Partido y código con el que pedir su equipo arbitral a una web (el del acta o el del proveedor de datos).
public sealed record OfficialsSource(Guid MatchId, string ExternalId);

public interface IRefereeStore
{
    Task<Referee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Referee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<Referee?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Referee>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(Referee referee);

    Task<IReadOnlyList<MatchOfficial>> GetOfficialsAsync(Guid matchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchOfficial>> GetOfficialsByMatchesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchOfficial>> GetOfficialsByRefereeAsync(Guid refereeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MatchOfficial>> GetAllOfficialsAsync(CancellationToken cancellationToken = default);
    void AddOfficial(MatchOfficial official);

    // Partidos de esos equipos con equipo arbitral desde esa fecha (para pedir sus notas).
    Task<IReadOnlyList<Guid>> GetMatchesWithOfficialsAsync(IEnumerable<Guid> teamIds, DateTimeOffset since, CancellationToken cancellationToken = default);

    // Actas guardadas de esa web cuyo partido aún no tiene equipo arbitral.
    Task<IReadOnlyList<OfficialsSource>> GetSheetsWithoutOfficialsAsync(string sheetProvider, CancellationToken cancellationToken = default);

    // Partidos jugados del proveedor de datos sin equipo arbitral que toca buscar en su web (nunca buscados, o buscados
    // hace más de retryAfter y menos de maxAttempts veces), los más recientes primero.
    Task<IReadOnlyList<OfficialsSource>> GetMatchesToLookUpAsync(
        string provider, DateTimeOffset now, TimeSpan retryAfter, int maxAttempts, int max, CancellationToken cancellationToken = default);
    Task<MatchOfficialLookup?> GetLookupAsync(Guid matchId, CancellationToken cancellationToken = default);
    void AddLookup(MatchOfficialLookup lookup);

    Task<IReadOnlyList<RefereeRating>> GetRatingsByMatchesAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefereeRating>> GetRatingsByRefereeAsync(Guid refereeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefereeRating>> GetAllRatingsAsync(CancellationToken cancellationToken = default);
    void AddRating(RefereeRating rating);
}
