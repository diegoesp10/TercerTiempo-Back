using System.ComponentModel;
using System.Globalization;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Sanctions;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Squads.Suspensions;

public sealed record DisciplineDto(
    [property: Description("Amarillas del ciclo de cinco que lleva antes del próximo partido (0 a 4): con 4 está apercibido y la siguiente le cuesta un partido.")]
    int CycleYellows,
    [property: Description("Si no puede estar en el próximo partido (ni jugarlo ni ir convocado, o en el banquillo si es del cuerpo técnico), por qué; null si puede.")]
    SuspensionBriefDto? Suspension)
{
    public bool Warned => Suspension is null && CycleYellows >= SuspensionCalendar.YellowsPerSuspension - 1;
}

public sealed record StaffDisciplineDto(
    [property: Description("Amarillas de la temporada en las actas oficiales cerradas (las dos de una doble amarilla cuentan).")]
    int YellowCards,
    [property: Description("Expulsiones de la temporada: rojas directas y dobles amarillas.")]
    int RedCards,
    [property: Description("Cómo llega al próximo partido: apercibido o sancionado.")]
    DisciplineDto Next);

// Una persona del cuerpo técnico en las actas oficiales de su equipo esta temporada: con qué nombres figura (con o sin
// segundo apellido), sus tarjetas y cómo llega al próximo partido.
public sealed record StaffPersonDiscipline(IReadOnlyList<string> FullNames, int YellowCards, int RedCards, DisciplineDto Next)
{
    public StaffDisciplineDto ToDto() => new(YellowCards, RedCards, Next);
}

public sealed record StaffDiscipline(Match? NextMatch, IReadOnlyList<StaffPersonDiscipline> People)
{
    // Un técnico de la app, por los nombres con los que se le reconoce (el de su perfil y el que escribió el club).
    public StaffPersonDiscipline? For(IEnumerable<string> normalizedNames)
    {
        var names = normalizedNames.Where(n => n.Length > 0).ToList();
        return People.FirstOrDefault(p => p.FullNames.Any(full => names.Any(n => PersonName.SameAsSheet(full, n))));
    }

    // Alguien que figura en las actas con ese nombre.
    public StaffPersonDiscipline? ForSheetName(string fullName)
    {
        var normalized = NameNormalizer.Normalize(PersonName.FromSheet(fullName));
        return People.FirstOrDefault(p => p.FullNames.Any(full => PersonName.SamePerson(NameNormalizer.Normalize(PersonName.FromSheet(full)), normalized)));
    }
}

// Las tarjetas y sanciones del cuerpo técnico de un equipo en la temporada, con lo que dicen las actas oficiales cerradas
// y el comité de competición. Como a los jugadores: cada cinco amarillas y cada expulsión cuestan un partido (el siguiente
// del equipo) y, cuando el comité le nombra por un partido, manda lo suyo (los partidos que pone o los días de
// inhabilitación, que le dejan fuera de los partidos de esos días). Las actas internas no llevan al cuerpo técnico.
internal sealed class StaffDisciplineReader(
    IMatchStore matches,
    IOfficialSheetStore sheets,
    ICommitteeSanctionStore committee,
    ITeamStore teams,
    TimeProvider clock)
{
    private const double MinTeamScore = 0.8;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public async Task<StaffDiscipline> LoadAsync(Guid teamId, Guid seasonId, CancellationToken cancellationToken) =>
        await LoadAsync(teamId, seasonId, await matches.GetTeamMatchesAsync(seasonId, teamId, cancellationToken), cancellationToken);

    public Task<StaffDiscipline> LoadAsync(Guid teamId, Guid seasonId, IReadOnlyList<Match> season, CancellationToken cancellationToken) =>
        LoadAsync(teamId, seasonId, season, null, cancellationToken);

    // staffSheets: las actas con cuerpo técnico de los partidos ya empezados, si quien llama ya las tiene (la ficha del
    // equipo); si no, se leen.
    public async Task<StaffDiscipline> LoadAsync(
        Guid teamId, Guid seasonId, IReadOnlyList<Match> season, IReadOnlyList<OfficialMatchSheet>? staffSheets, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var next = NextMatch(season, now);
        var past = season.Where(m => m.KickoffAt <= now && m.Status is not (MatchStatus.Cancelled or MatchStatus.Postponed)).ToList();
        if (past.Count == 0) return new StaffDiscipline(next, []);

        var pastIds = past.Select(m => m.Id).ToHashSet();
        var official = (staffSheets ?? await sheets.GetStaffByMatchIdsAsync(pastIds, cancellationToken))
            .Where(s => s.IsAvailable && pastIds.Contains(s.MatchId))
            .GroupBy(s => s.MatchId)
            .ToDictionary(g => g.Key, g => g.First());

        var people = new List<Person>();
        var cards = new Dictionary<Guid, IReadOnlyDictionary<Guid, MatchCards>>();
        foreach (var match in past)
        {
            if (!official.TryGetValue(match.Id, out var sheet)) continue;
            var byPerson = new Dictionary<Guid, MatchCards>();
            foreach (var row in sheet.StaffOf(SideOf(match, teamId)))
            {
                var person = Person.Of(people, row.FullName);
                // Quien figura en dos funciones lleva las tarjetas en una sola.
                byPerson[person.Key] = byPerson.TryGetValue(person.Key, out var other)
                    ? new MatchCards(Math.Max(other.YellowCards, row.YellowCards), other.RedCard || row.RedCard)
                    : new MatchCards(row.YellowCards, row.RedCard);
            }
            if (byPerson.Count > 0) cards[match.Id] = byPerson;
        }
        if (people.Count == 0) return new StaffDiscipline(next, []);

        var (decisions, bans) = await CommitteeAsync(teamId, seasonId, past, official, people, cancellationToken);
        var calendar = SuspensionCalendar.Build(season, cards, decisions);

        var result = people.Select(person =>
        {
            var mine = cards.Values.Select(c => c.GetValueOrDefault(person.Key)).OfType<MatchCards>().ToList();
            return new StaffPersonDiscipline(
                person.FullNames,
                mine.Sum(c => c.YellowCards),
                mine.Count(c => c.RedCard),
                Next(person, calendar, bans, next));
        }).ToList();
        return new StaffDiscipline(next, result);
    }

    // El próximo partido de liga del equipo: el que se está jugando o el primero por jugar (los aplazados no cuentan).
    public static Match? NextMatch(IEnumerable<Match> season, DateTimeOffset now) => season
        .Where(m => m.Status == MatchStatus.InProgress || (m.Status == MatchStatus.Scheduled && m.KickoffAt > now))
        .OrderBy(m => m.KickoffAt)
        .FirstOrDefault();

    private static DisciplineDto Next(Person person, SuspensionCalendar calendar, IReadOnlyList<DayBan> bans, Match? next)
    {
        if (next is null) return new DisciplineDto(0, null);

        var suspension = calendar.On(person.Key, next.Id) is { } due ? SuspensionReader.Brief(due) : null;
        if (suspension is null)
        {
            var day = SpainTime.Today(next.KickoffAt);
            var ban = bans.Where(b => b.Person == person.Key && day < b.Until).OrderByDescending(b => b.Until).FirstOrDefault();
            if (ban is not null)
                suspension = new SuspensionBriefDto(SuspensionCause.Committee, ban.Reason, ban.FromMatchId, ban.FromMatchday, Until: ban.Until.AddDays(-1));
        }
        return new DisciplineDto(calendar.YellowsBefore(person.Key, next.Id), suspension);
    }

    // Lo que el comité ha decidido del cuerpo técnico en cada partido ya visto (como a los jugadores: su jornada en una
    // reunión del mismo día o posterior), con los nombres de la federación cruzados con los de las actas del equipo.
    private async Task<(IReadOnlyDictionary<Guid, CommitteeDecision> Decisions, IReadOnlyList<DayBan> Bans)> CommitteeAsync(
        Guid teamId, Guid seasonId, IReadOnlyList<Match> past, IReadOnlyDictionary<Guid, OfficialMatchSheet> official, IReadOnlyList<Person> people,
        CancellationToken cancellationToken)
    {
        var resolutions = await committee.GetBySeasonAsync(seasonId, cancellationToken);
        var decisions = new Dictionary<Guid, CommitteeDecision>();
        var bans = new List<DayBan>();
        if (resolutions.Count == 0) return (decisions, bans);

        var ourNames = await TeamNamesAsync(teamId, past, official, resolutions, cancellationToken);
        if (ourNames.Count == 0) return (decisions, bans);

        foreach (var match in past)
        {
            var playedOn = SpainTime.Today(match.KickoffAt);
            var seen = resolutions.Where(r => r.Matchday == match.Matchday && r.MeetingOn >= playedOn).OrderBy(r => r.MeetingOn).ToList();
            if (seen.Count == 0) continue;

            var penalties = new Dictionary<Guid, List<CommitteePenalty>>();
            foreach (var resolution in seen)
            {
                foreach (var sanction in resolution.Sanctions.Where(s => s.Kind == CommitteeSanctionKind.Staff && ourNames.Contains(NameNormalizer.Normalize(s.TeamName))))
                {
                    if (Person.Find(people, sanction.PersonName) is not { } person) continue;
                    if (!penalties.TryGetValue(person.Key, out var list)) penalties[person.Key] = list = [];
                    list.Add(new CommitteePenalty(sanction.Matches, sanction.Reason, resolution.MeetingOn));
                    if (sanction.Days is { } days)
                        bans.Add(new DayBan(person.Key, resolution.MeetingOn.AddDays(days), match.Id, match.Matchday, BanReason(days, match.Matchday, sanction.Reason, resolution.MeetingOn.AddDays(days - 1))));
                }
            }
            decisions[match.Id] = penalties.Count == 0
                ? CommitteeDecision.Nothing
                : new CommitteeDecision(penalties.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<CommitteePenalty>)kv.Value));
        }
        return (decisions, bans);
    }

    private static string BanReason(int days, int matchday, string detail, DateOnly lastDay)
    {
        var text = $"Inhabilitación de {days} días del comité por la jornada {matchday}";
        if (detail.Length > 0) text += $": {char.ToLowerInvariant(detail[0])}{detail[1..]}";
        return $"{text} (hasta el {lastDay.ToString("d 'de' MMMM", Spanish)})";
    }

    // Como en las sanciones de los jugadores: el nombre del equipo en sus actas oficiales o, sin ninguna, el más parecido de
    // los que salen en las sanciones (si es claro).
    private async Task<IReadOnlySet<string>> TeamNamesAsync(
        Guid teamId, IReadOnlyList<Match> past, IReadOnlyDictionary<Guid, OfficialMatchSheet> official, IReadOnlyList<CommitteeResolution> resolutions,
        CancellationToken cancellationToken)
    {
        var names = past
            .Where(m => official.ContainsKey(m.Id))
            .Select(m => NameNormalizer.Normalize(m.IsHome(teamId) ? official[m.Id].HomeTeamName : official[m.Id].AwayTeamName))
            .Where(n => n.Length > 0)
            .ToHashSet();
        if (names.Count > 0) return names;

        if (await teams.GetByIdAsync(teamId, cancellationToken) is not { } team) return names;
        var scored = resolutions
            .SelectMany(r => r.Sanctions.Select(s => s.TeamName))
            .Distinct()
            .Select(n => (Name: n, Score: TeamNameSimilarity.Score(team.Name, n)))
            .Where(n => n.Score >= MinTeamScore)
            .OrderByDescending(n => n.Score)
            .ToList();
        if (scored.Count > 0 && (scored.Count == 1 || scored[1].Score < scored[0].Score))
            names.Add(NameNormalizer.Normalize(scored[0].Name));
        return names;
    }

    private static MatchSide SideOf(Match match, Guid teamId) => match.IsHome(teamId) ? MatchSide.Home : MatchSide.Away;

    // Until: el primer día en que ya puede volver.
    private sealed record DayBan(Guid Person, DateOnly Until, Guid FromMatchId, int FromMatchday, string Reason);

    private sealed class Person(string normalized)
    {
        public Guid Key { get; } = Guid.NewGuid();
        public string Normalized { get; } = normalized;
        public List<string> FullNames { get; } = [];

        // La misma persona puede venir escrita de dos formas en actas distintas (con o sin el segundo apellido).
        public static Person Of(List<Person> people, string fullName)
        {
            var person = Find(people, fullName);
            if (person is null)
            {
                person = new Person(NameNormalizer.Normalize(PersonName.FromSheet(fullName)));
                people.Add(person);
            }
            if (!person.FullNames.Contains(fullName)) person.FullNames.Add(fullName);
            return person;
        }

        public static Person? Find(IReadOnlyList<Person> people, string fullName)
        {
            var normalized = NameNormalizer.Normalize(PersonName.FromSheet(fullName));
            return people.FirstOrDefault(p => p.Normalized == normalized)
                ?? people.FirstOrDefault(p => PersonName.SamePerson(p.Normalized, normalized));
        }
    }
}
