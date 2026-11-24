using System.Security.Cryptography;
using System.Text;
using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Sanctions;

// Una sanción tal como la publica el comité: su equipo y su nombre como en la federación, el artículo del reglamento, el
// motivo y lo que cuesta: partidos de suspensión o, al cuerpo técnico, también días de inhabilitación.
public sealed record CommitteeSanctionData(
    string TeamName, string PersonName, string Article, string Reason, int Matches,
    CommitteeSanctionKind Kind = CommitteeSanctionKind.Player, int? Days = null);

// A quién: a un jugador o a alguien del cuerpo técnico (técnicos, auxiliares y delegados).
public enum CommitteeSanctionKind
{
    Player = 1,
    Staff = 2,
}

// Lo que publica el comité de competición de la federación sobre una jornada de un grupo en una de sus reuniones: los
// partidos de suspensión de los jugadores y los del cuerpo técnico, con sus inhabilitaciones por días. No se guardan las
// amonestaciones (salen de las actas), las inhabilitaciones por días de los jugadores ni lo de los equipos. Es público:
// vive en la base de datos reales y cada entorno tiene su copia.
// Una reunión puede tratar varias jornadas y una jornada puede volver a una reunión posterior (un recurso, un partido
// aplazado): cada pareja reunión-jornada es una resolución.
public sealed class CommitteeResolution : AggregateRoot
{
    public const int MaxCommitteeLength = 150;

    private readonly List<CommitteeSanction> _sanctions = [];

    public Guid SeasonId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Committee { get; private set; } = string.Empty;
    public DateOnly MeetingOn { get; private set; }
    public int Matchday { get; private set; }
    public DateOnly? MatchdayOn { get; private set; }

    // Huella de las sanciones publicadas: si la federación cambia alguna, se sustituyen todas.
    public string Fingerprint { get; private set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<CommitteeSanction> Sanctions => _sanctions.AsReadOnly();

    private CommitteeResolution() { }

    public static CommitteeResolution Create(
        Guid seasonId, string provider, string committee, DateOnly meetingOn, int matchday, DateOnly? matchdayOn,
        IReadOnlyCollection<CommitteeSanctionData> sanctions, DateTimeOffset now)
    {
        var resolution = new CommitteeResolution
        {
            Id = NewId(),
            SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)),
            Provider = Guard.NotEmpty(provider, nameof(provider), 50),
            Committee = Guard.NotEmpty(committee, "comité", MaxCommitteeLength),
            MeetingOn = meetingOn,
            Matchday = Guard.InRange(matchday, 1, 99, "jornada"),
            FirstSeenAt = now,
        };
        resolution.Apply(matchdayOn, sanctions, now);
        return resolution;
    }

    // Lo que publica ahora la federación; devuelve si ha cambiado algo.
    public bool Apply(DateOnly? matchdayOn, IReadOnlyCollection<CommitteeSanctionData> sanctions, DateTimeOffset now)
    {
        var fingerprint = FingerprintOf(sanctions);
        if (fingerprint == Fingerprint && matchdayOn == MatchdayOn) return false;

        MatchdayOn = matchdayOn;
        if (fingerprint != Fingerprint)
        {
            _sanctions.Clear();
            _sanctions.AddRange(sanctions.Select(s => CommitteeSanction.Create(Id, s)));
            Fingerprint = fingerprint;
        }
        UpdatedAt = now;
        return true;
    }

    // Las de los jugadores con la misma huella de siempre: lo ya guardado no cambia por saber leer más.
    public static string FingerprintOf(IEnumerable<CommitteeSanctionData> sanctions)
    {
        var lines = sanctions
            .Select(s => $"{s.TeamName}|{s.PersonName}|{s.Article}|{s.Reason}|{s.Matches}"
                + (s.Kind == CommitteeSanctionKind.Staff ? "|staff" : string.Empty)
                + (s.Days is { } days ? $"|{days}d" : string.Empty))
            .Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }
}

public sealed class CommitteeSanction : Entity
{
    public const int MaxNameLength = 150;
    public const int MaxArticleLength = 30;
    public const int MaxReasonLength = 500;

    public Guid ResolutionId { get; private set; }
    public string TeamName { get; private set; } = string.Empty;
    public string PersonName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Article { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;

    // Partidos de suspensión; 0 en una inhabilitación por días (Days), que solo se guarda al cuerpo técnico.
    public int Matches { get; private set; }
    public CommitteeSanctionKind Kind { get; private set; } = CommitteeSanctionKind.Player;
    public int? Days { get; private set; }

    private CommitteeSanction() { }

    internal static CommitteeSanction Create(Guid resolutionId, CommitteeSanctionData data)
    {
        var name = Guard.NotEmpty(data.PersonName, data.Kind == CommitteeSanctionKind.Staff ? "técnico" : "jugador", MaxNameLength);
        var days = data.Kind == CommitteeSanctionKind.Staff && data.Days is { } d ? Guard.InRange(d, 1, 999, "días") : (int?)null;
        return new CommitteeSanction
        {
            Id = NewId(),
            ResolutionId = resolutionId,
            TeamName = Guard.NotEmpty(data.TeamName, "equipo", MaxNameLength),
            PersonName = name,
            // Como las líneas de las actas: "APELLIDOS, NOMBRE" sin la coma.
            NormalizedName = NameNormalizer.Normalize(name.Replace(',', ' ')),
            Article = data.Article.Length > MaxArticleLength ? data.Article[..MaxArticleLength] : data.Article,
            Reason = Guard.NotEmpty(data.Reason, "motivo", MaxReasonLength),
            Matches = Guard.InRange(data.Matches, days is null ? 1 : 0, 99, "partidos"),
            Kind = data.Kind,
            Days = days,
        };
    }
}

// Cuándo se miró por última vez la página de sanciones de un grupo y hasta qué jornada había publicado el comité. Es
// estado de la descarga: solo vive en la base de datos reales.
public sealed class CommitteeSanctionCheck
{
    public Guid SeasonId { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }
    public int? PublishedMatchday { get; private set; }

    private CommitteeSanctionCheck() { }

    public static CommitteeSanctionCheck First(Guid seasonId, int? publishedMatchday, DateTimeOffset now) =>
        new() { SeasonId = Guard.NotEmpty(seasonId, nameof(seasonId)), CheckedAt = now, PublishedMatchday = publishedMatchday };

    // publishedMatchday null: la página no se pudo leer; se apunta la hora para no volver a pedirla enseguida.
    public void Checked(int? publishedMatchday, DateTimeOffset now)
    {
        CheckedAt = now;
        if (publishedMatchday is not null) PublishedMatchday = publishedMatchday;
    }
}

public interface ICommitteeSanctionStore
{
    // Las de esa temporada de liga con sus sanciones, solo para leer.
    Task<IReadOnlyList<CommitteeResolution>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // Lo mismo, para modificarlas (la descarga).
    Task<IReadOnlyList<CommitteeResolution>> GetStoredBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    // Temporadas de liga con alguna reunión del comité desde ese día.
    Task<IReadOnlyList<Guid>> GetSeasonsWithMeetingsSinceAsync(DateOnly since, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitteeSanctionCheck>> GetChecksAsync(CancellationToken cancellationToken = default);

    void Add(CommitteeResolution resolution);
    void Remove(CommitteeResolution resolution);
    void AddCheck(CommitteeSanctionCheck check);
}
