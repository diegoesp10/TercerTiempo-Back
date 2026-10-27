using System.Diagnostics;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Players;

namespace TercerTiempo.Application.Search;

public static class SearchTypes
{
    public const string Competition = "competition";
    public const string Team = "team";
    public const string Club = "club";
    public const string Player = "player";
    public const string Match = "match";
    public const string Squad = "squad";
    public const string Referee = "referee";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Squad] = "Tus plantillas",
        [Competition] = "Ligas",
        [Team] = "Equipos",
        [Club] = "Clubes",
        [Player] = "Jugadores",
        [Match] = "Partidos",
        [Referee] = "Árbitros",
    };

    public static bool IsKnown(string type) => Labels.ContainsKey(type);
}

// Quién busca: sus equipos suben en los resultados y solo él ve sus plantillas internas (las de su cuerpo técnico o la
// suya como jugador y, para un club, todas las de sus equipos).
internal sealed record SearchViewer(IReadOnlySet<Guid> OwnTeamIds, IReadOnlySet<Guid> VisibleSquadIds, IReadOnlySet<Guid> ClubTeamIds)
{
    public bool CanSee(Guid squadId, Guid? teamId) => VisibleSquadIds.Contains(squadId) || teamId is { } team && ClubTeamIds.Contains(team);
}

internal sealed record SearchHit(SearchDoc Doc, double Score, string Link, IReadOnlyList<string> Badges);

internal sealed record SearchOutcome(IReadOnlyList<string> Terms, bool Partial, IReadOnlyList<SearchHit> Hits, double ElapsedMs);

internal sealed class SearchDoc
{
    // Peso de cada campo: el nombre manda; los demás (liga, equipo del jugador, dorsal) solo ayudan a afinar.
    public static readonly double[] FieldWeights = [1.0, 0.55, 0.3];

    public required string Type { get; init; }
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string Link { get; init; }
    public string? ImageUrl { get; init; }
    public string ImageKind { get; init; } = "none";
    public string? SecondaryImageUrl { get; init; }
    public string? Meta { get; init; }
    public DateTimeOffset? Date { get; init; }
    public IReadOnlyList<string> Badges { get; init; } = [];
    public double Prior { get; init; }
    public required string NormalizedTitle { get; init; }
    // Palabras por campo. En los partidos, 0 = local y 1 = visitante (los dos con el peso del nombre) y 2 = la liga.
    public required string[][] Fields { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? AwayTeamId { get; init; }
    public Guid? RestrictedToSquadId { get; init; }
    public Guid? RosterEntryId { get; init; }
}

// Índice en memoria: documentos y, por cada palabra, dónde aparece. Buscar recorre el vocabulario una vez por palabra de
// la búsqueda (prefijos y erratas) y suma lo encontrado en cada documento.
internal sealed class SearchIndex
{
    private readonly List<SearchDoc> _docs;
    private readonly string[] _vocabulary;
    private readonly Dictionary<string, List<(int Doc, int Field)>> _postings;
    // Ficha publicada → su jugador en una plantilla interna (para llevar a su perfil a quien puede verlo).
    private readonly Dictionary<Guid, (Guid SquadId, Guid SquadPlayerId, Guid TeamId)> _rosterInSquads;

    public DateTimeOffset BuiltAt { get; }
    public int Count => _docs.Count;

    private SearchIndex(List<SearchDoc> docs, Dictionary<Guid, (Guid, Guid, Guid)> rosterInSquads, DateTimeOffset builtAt)
    {
        _docs = docs;
        _rosterInSquads = rosterInSquads;
        BuiltAt = builtAt;
        _postings = [];
        for (var d = 0; d < docs.Count; d++)
            for (var f = 0; f < docs[d].Fields.Length; f++)
                foreach (var word in docs[d].Fields[f])
                {
                    if (!_postings.TryGetValue(word, out var list)) _postings[word] = list = [];
                    list.Add((d, f));
                }
        _vocabulary = [.. _postings.Keys];
    }

    public static SearchIndex Build(SearchCorpus corpus, DateTimeOffset now)
    {
        var docs = new List<SearchDoc>();
        var competitions = corpus.Competitions.ToDictionary(c => c.Id);
        var teams = corpus.Teams.ToDictionary(t => t.Id);
        var teamClub = corpus.Clubs.SelectMany(c => c.TeamIds.Select(t => (t, c))).GroupBy(x => x.t).ToDictionary(g => g.Key, g => g.First().c);

        foreach (var c in corpus.Competitions)
        {
            var group = c.Group is { Length: > 0 } g ? $"Grupo {g}" : null;
            docs.Add(new SearchDoc
            {
                Type = SearchTypes.Competition, Id = c.Id, Title = c.Name, NormalizedTitle = SearchText.Normalize(c.Name),
                Subtitle = Join($"{c.Teams} equipos", c.Region, c.SeasonLabel is null ? null : $"Temporada {c.SeasonLabel}",
                    c.Played > 0 ? $"{c.Played} partidos jugados" : null),
                Link = $"/liga?competition={c.Id}", ImageKind = "league",
                Meta = c.Matchdays > 0 ? $"{c.Matchdays} jornadas" : null,
                Prior = 1.2 - Math.Min(c.SortOrder, 20) * 0.01,
                Fields = [SearchText.WithOrdinals(SearchText.Tokens(c.Name)), SearchText.WithOrdinals(SearchText.Tokens($"{c.Category} {group} liga {c.Region}")), []],
            });
        }

        foreach (var t in corpus.Teams)
        {
            var competition = t.CompetitionId is { } cid ? competitions.GetValueOrDefault(cid) : null;
            var club = teamClub.GetValueOrDefault(t.Id);
            docs.Add(new SearchDoc
            {
                Type = SearchTypes.Team, Id = t.Id, Title = t.Name, NormalizedTitle = SearchText.Normalize(t.Name),
                Subtitle = competition is null ? "Sin liga en curso"
                    : Join(competition.Name, t.Position is { } p ? $"{p}º con {t.Points ?? 0} puntos" : null),
                Link = $"/equipos/{t.Id}", ImageUrl = t.CrestUrl, ImageKind = "crest",
                Badges = club is null ? [] : ["Club en Tercer Tiempo"],
                Prior = competition is null ? 0.4 : 1.0,
                Fields = [SearchText.Tokens(t.Name), SearchText.Tokens(t.ShortName), SearchText.WithOrdinals(SearchText.Tokens(competition?.Name))],
                TeamId = t.Id,
            });
        }

        foreach (var c in corpus.Clubs)
        {
            var clubTeams = c.TeamIds.Select(id => teams.GetValueOrDefault(id)).OfType<CorpusTeam>().ToList();
            var first = clubTeams.FirstOrDefault();
            if (first is null) continue;
            docs.Add(new SearchDoc
            {
                Type = SearchTypes.Club, Id = c.Id, Title = c.Name, NormalizedTitle = SearchText.Normalize(c.Name),
                Subtitle = clubTeams.Count == 1 ? "Club con cuenta · 1 equipo" : $"Club con cuenta · {clubTeams.Count} equipos: {string.Join(", ", clubTeams.Select(t => t.Name))}",
                Link = $"/equipos/{first.Id}", ImageUrl = clubTeams.Select(t => t.CrestUrl).FirstOrDefault(u => u is not null), ImageKind = "crest",
                Prior = 0.8,
                Fields = [SearchText.Tokens(c.Name), SearchText.Tokens(string.Join(' ', clubTeams.Select(t => t.Name))), []],
                TeamId = first.Id,
            });
        }

        foreach (var r in corpus.Referees ?? [])
        {
            docs.Add(new SearchDoc
            {
                Type = SearchTypes.Referee, Id = r.Id, Title = r.Name, NormalizedTitle = SearchText.Normalize(r.Name),
                Subtitle = string.Join(" · ", new[] { r.Assistant ? "Asistente" : "Árbitro", r.Category, r.Matches == 1 ? "1 partido" : $"{r.Matches} partidos" }.OfType<string>()),
                Link = $"/arbitros/{r.Id}", ImageKind = "referee", Prior = 0.6,
                Fields = [SearchText.Tokens(r.Name), [], []],
            });
        }

        foreach (var p in corpus.Players)
        {
            if (!teams.TryGetValue(p.TeamId, out var team)) continue;
            docs.Add(PlayerDoc(p.EntryId, p.FullName, p.Position, p.ShirtNumber, p.PhotoUrl, team, $"/equipos/{team.Id}/jugadores/{p.EntryId}", null, p.EntryId));
        }

        var rosterInSquads = new Dictionary<Guid, (Guid, Guid, Guid)>();
        var rosterIds = corpus.Players.Select(p => p.EntryId).ToHashSet();
        foreach (var s in corpus.Squads)
        {
            if (!teams.TryGetValue(s.TeamId, out var team)) continue;
            docs.Add(new SearchDoc
            {
                Type = SearchTypes.Squad, Id = s.Id, Title = $"Plantilla de {team.Name}", NormalizedTitle = SearchText.Normalize(team.Name),
                Subtitle = Join($"Temporada {s.SeasonLabel}", s.Players.Count == 1 ? "1 jugador" : $"{s.Players.Count} jugadores"),
                Link = $"/plantillas/{s.Id}", ImageUrl = team.CrestUrl, ImageKind = "crest", Prior = 1.5,
                Fields = [SearchText.Tokens(team.Name), SearchText.Tokens($"plantilla {team.ShortName}"), []],
                TeamId = team.Id, RestrictedToSquadId = s.Id,
            });

            foreach (var player in s.Players)
            {
                if (player.RosterEntryId is { } entry && rosterIds.Contains(entry))
                {
                    rosterInSquads[entry] = (s.Id, player.Id, team.Id);
                    continue;
                }
                docs.Add(PlayerDoc(player.Id, player.FullName, player.Position, player.ShirtNumber, player.PhotoUrl, team,
                    $"/plantillas/{s.Id}/jugadores/{player.Id}", s.Id, null));
            }
        }

        foreach (var m in corpus.Matches)
        {
            if (!teams.TryGetValue(m.HomeTeamId, out var home) || !teams.TryGetValue(m.AwayTeamId, out var away)) continue;
            var competition = competitions.GetValueOrDefault(m.CompetitionId);
            var played = m.Status == MatchStatus.Played && m.HomeGoals is not null;
            docs.Add(new SearchDoc
            {
                Type = SearchTypes.Match, Id = m.Id, Title = $"{home.Name} – {away.Name}", NormalizedTitle = SearchText.Normalize($"{home.Name} {away.Name}"),
                Subtitle = Join(competition?.Name, $"Jornada {m.Matchday}", m.Status switch
                {
                    MatchStatus.Postponed => "Aplazado",
                    MatchStatus.Cancelled => "Suspendido",
                    MatchStatus.InProgress => "En juego",
                    _ => null,
                }),
                Link = $"/partidos/{m.Id}", ImageUrl = home.CrestUrl, SecondaryImageUrl = away.CrestUrl, ImageKind = "match",
                Meta = played ? $"{m.HomeGoals}-{m.AwayGoals}" : "vs", Date = m.KickoffAt,
                // Los más cercanos a hoy (la última jornada y la próxima) van primero.
                Prior = 0.2 + 1.0 / (1 + Math.Abs((m.KickoffAt - now).TotalDays) / 10),
                Fields = [SearchText.Tokens(home.Name), SearchText.Tokens(away.Name), SearchText.Tokens($"{competition?.Name} j{m.Matchday} jornada {m.Matchday}")],
                TeamId = home.Id, AwayTeamId = away.Id,
            });
        }

        return new SearchIndex(docs, rosterInSquads, now);
    }

    private static SearchDoc PlayerDoc(Guid id, string name, Position? position, int? shirt, string? photo, CorpusTeam team, string link, Guid? squadId, Guid? rosterEntryId) => new()
    {
        Type = SearchTypes.Player, Id = id, Title = name, NormalizedTitle = SearchText.Normalize(name),
        Subtitle = Join(team.Name, PositionLabel(position), shirt is { } n ? $"Dorsal {n}" : null),
        Link = link, ImageUrl = photo, ImageKind = "photo", SecondaryImageUrl = team.CrestUrl,
        Prior = 0.3,
        Fields = [SearchText.Tokens(name), SearchText.Tokens(team.Name), shirt is { } d ? [d.ToString(), $"dorsal{d}"] : []],
        TeamId = team.Id, RestrictedToSquadId = squadId, RosterEntryId = rosterEntryId,
    };

    public SearchOutcome Search(string query, SearchViewer viewer, string? type)
    {
        var watch = Stopwatch.StartNew();
        var terms = SearchText.QueryTokens(query);
        if (terms.Length == 0 || terms.All(t => t.Length < 2 && !t.All(char.IsDigit)))
            return new SearchOutcome(terms, false, [], 0);

        var required = terms.Where(t => !SearchText.IsGeneric(t)).ToArray();
        if (required.Length == 0) required = terms;
        var requiredIndex = required.Select(t => Array.IndexOf(terms, t)).ToArray();

        // best[doc][término] = mejor coincidencia ponderada; sides[doc][término] = en qué equipo de un partido ha
        // coincidido (1 = local, 2 = visitante, 3 = los dos).
        var best = new Dictionary<int, double[]>();
        var sides = new Dictionary<int, int[]>();
        for (var q = 0; q < terms.Length; q++)
        {
            foreach (var word in _vocabulary)
            {
                var similarity = SearchText.Similarity(terms[q], word);
                if (similarity <= 0) continue;
                foreach (var (doc, field) in _postings[word])
                {
                    var d = _docs[doc];
                    if (type is not null && d.Type != type) continue;
                    var weight = d.Type == SearchTypes.Match && field < 2 ? 1.0 : SearchDoc.FieldWeights[field];
                    var score = similarity * weight * (SearchText.IsGeneric(terms[q]) ? 0.4 : 1);
                    if (!best.TryGetValue(doc, out var scores)) best[doc] = scores = new double[terms.Length];
                    if (score > scores[q]) scores[q] = score;
                    if (d.Type == SearchTypes.Match && field < 2)
                    {
                        if (!sides.TryGetValue(doc, out var side)) sides[doc] = side = new int[terms.Length];
                        side[q] |= field + 1;
                    }
                }
            }
        }

        var normalizedQuery = string.Join(' ', terms);
        IEnumerable<SearchHit> Collect(int missingAllowed) => best
            .Where(kv => requiredIndex.Count(q => kv.Value[q] <= 0) <= missingAllowed)
            .Select(kv => Score(kv.Key, kv.Value, sides.GetValueOrDefault(kv.Key), terms, normalizedQuery, viewer))
            .OfType<SearchHit>();

        var hits = Collect(0).ToList();
        var partial = false;
        if (hits.Count == 0 && required.Length >= 2)
        {
            hits = Collect(1).ToList();
            partial = hits.Count > 0;
        }

        hits.Sort((a, b) => b.Score.CompareTo(a.Score) is var c && c != 0 ? c : string.Compare(a.Doc.Title, b.Doc.Title, StringComparison.CurrentCulture));
        return new SearchOutcome(terms, partial, hits, watch.Elapsed.TotalMilliseconds);
    }

    private SearchHit? Score(int index, double[] scores, int[]? sides, string[] terms, string normalizedQuery, SearchViewer viewer)
    {
        var doc = _docs[index];
        if (doc.RestrictedToSquadId is { } squad && !viewer.CanSee(squad, doc.TeamId)) return null;

        // Un partido solo sale si la búsqueda nombra a cada equipo con una palabra propia («nalón roces»): «real avilés»
        // es un equipo, no el Real Avilés – Real Unión, y una letra suelta no nombra a nadie.
        if (doc.Type == SearchTypes.Match)
        {
            bool Names(int side) => sides is not null
                && Enumerable.Range(0, terms.Length).Any(q => sides[q] == side && terms[q].Length >= 3 && !SearchText.IsGeneric(terms[q]));
            if (!Names(1) || !Names(2)) return null;
        }

        var score = scores.Sum() / terms.Length * 10 + doc.Prior;
        if (doc.NormalizedTitle == normalizedQuery) score += 4;
        else if (doc.NormalizedTitle.StartsWith(normalizedQuery, StringComparison.Ordinal)) score += 2;

        var badges = doc.Badges.ToList();
        var link = doc.Link;
        if (doc.TeamId is { } team && viewer.OwnTeamIds.Contains(team) || doc.AwayTeamId is { } away && viewer.OwnTeamIds.Contains(away))
        {
            score += 1.5;
            badges.Insert(0, doc.Type == SearchTypes.Player ? "De tu equipo" : doc.Type == SearchTypes.Match ? "Partido tuyo" : "Tu equipo");
        }
        if (doc.RestrictedToSquadId is not null && doc.Type == SearchTypes.Player) badges.Insert(0, "Tu plantilla");

        // La ficha publicada de un jugador de una plantilla que puedes ver lleva a su perfil interno.
        if (doc.RosterEntryId is { } entry && _rosterInSquads.TryGetValue(entry, out var inSquad) && viewer.CanSee(inSquad.SquadId, inSquad.TeamId))
        {
            link = $"/plantillas/{inSquad.SquadId}/jugadores/{inSquad.SquadPlayerId}";
            if (!badges.Contains("Tu plantilla")) badges.Insert(0, "Tu plantilla");
            score += 0.5;
        }

        return new SearchHit(doc, Math.Round(score, 3), link, badges);
    }

    private static string? PositionLabel(Position? position) => position switch
    {
        Position.Goalkeeper => "Portero",
        Position.Defender => "Defensa",
        Position.Midfielder => "Centrocampista",
        Position.Forward => "Delantero",
        _ => null,
    };

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
