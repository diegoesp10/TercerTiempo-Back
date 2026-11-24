using System.ComponentModel;
using System.Globalization;
using TercerTiempo.Application.Abstractions.Identity;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Common;
using TercerTiempo.Application.MatchReports;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Trainings;
using TercerTiempo.Domain.Alerts;
using TercerTiempo.Domain.Clubs;
using TercerTiempo.Domain.Coaches;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;
using TercerTiempo.Domain.MatchReports;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Domain.Teams;
using Regex = System.Text.RegularExpressions.Regex;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;

namespace TercerTiempo.Application.Squads.Injuries;

public sealed record InjuryProposalDto(
    Guid Id,
    Guid SquadPlayerId,
    string PlayerName,
    Guid MatchId,
    int Matchday,
    // No MatchDay: el JSON no distingue mayúsculas y chocaría con Matchday (la jornada).
    [property: Description("Día del partido (hora de España): el parte se propone con este día.")]
    DateOnly MatchDate,
    string HomeTeam,
    string AwayTeam,
    [property: Description("Minuto en que dejó el campo según el acta oficial; null si acabó el partido.")]
    int? Minute,
    [property: Description("La frase de la crónica que lo cuenta.")]
    string Excerpt,
    [property: Description("Lo que le pasó, como lo dice la crónica (\"luxación en su codo izquierdo\").")]
    string Phrase,
    string SourceUrl,
    [property: Description("Tipo de lesión que encaja con lo que dice la crónica (código del catálogo); null si ninguno.")]
    string? SuggestedType,
    [property: Description("Sitio del cuerpo que nombra la frase de la crónica (código del catálogo), entre los que pregunta el tipo sugerido; con sitio fijo, ese si no precisa más; null si no lo dice.")]
    string? SuggestedSite,
    [property: Description("Lado que dice la frase de la crónica (Left o Right), si el sitio tiene dos; null si no lo dice.")]
    BodySide? SuggestedSide,
    DateTimeOffset DetectedAt);

public sealed record MatchInjuryDto(
    Guid SquadPlayerId,
    string PlayerName,
    int? Minute,
    string Phrase,
    [property: Description("true: ya registrada como lesión; false: posible lesión sin confirmar (solo la ve el cuerpo técnico).")]
    bool Confirmed,
    Guid? InjuryId,
    Guid? ProposalId,
    string? TypeLabel,
    [property: Description("Su línea en el acta oficial, para marcarlo también en la alineación; null si no se encuentra.")]
    Guid? SheetLineId = null,
    [property: Description("Dónde fue la lesión registrada (\"Rodilla izquierda\", \"Ligamento cruzado anterior (LCA), rodilla derecha\"); null si no lo dice o es una posible lesión.")]
    string? Location = null);

public sealed record SyncInjuryProposalsCommand : ICommand<InjuryProposalsSynced>;

public sealed record InjuryProposalsSynced(int Mentions, int Created, int Refreshed, int Expired = 0);

public sealed record ListInjuryProposalsQuery(Guid SquadId) : IQuery<IReadOnlyList<InjuryProposalDto>>;

public sealed record DismissInjuryProposalCommand(Guid SquadId, Guid ProposalId) : ICommand<IReadOnlyList<InjuryProposalDto>>;

public sealed record ListMatchInjuriesQuery(Guid SquadId, Guid MatchId) : IQuery<IReadOnlyList<MatchInjuryDto>>;

// Las posibles lesiones de las crónicas (copiadas de los datos reales) para las plantillas de este entorno: cada jugador
// nombrado se busca en la plantilla de su equipo por su línea del acta oficial (como en el resto de la app) y, si no tiene
// ya una lesión de esos días, se le propone al club y al cuerpo técnico con un aviso. No llama a ninguna web.
internal sealed class SyncInjuryProposalsHandler(
    IMatchReportStore reports,
    IMatchStore matches,
    ITeamStore teams,
    ISquadStore squads,
    IOfficialSheetStore sheets,
    IInjuryStore injuries,
    IInjuryProposalStore proposals,
    IClubStore clubs,
    IAlertStore alerts,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncInjuryProposalsCommand, InjuryProposalsSynced>
{
    internal const int LookBackDays = 21;
    // Las que nadie registró (pendientes o descartadas) se borran a los 60 días de detectarse, con su aviso si seguía abierto.
    // Una pendiente se retira antes si la crónica ya no lo cuenta (se corrigió o ya no se lee así).
    internal static readonly TimeSpan Keep = TimeSpan.FromDays(60);
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public async Task<InjuryProposalsSynced> Handle(SyncInjuryProposalsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var expired = await RetireAsync(now, cancellationToken);
        var mentions = await reports.GetUpdatedSinceAsync(now.AddDays(-LookBackDays), cancellationToken);
        if (mentions.Count == 0)
        {
            if (expired > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
            return new InjuryProposalsSynced(0, 0, 0, expired);
        }

        var matchIds = mentions.Select(m => m.MatchId).Distinct().ToList();
        var matchMap = (await matches.GetByIdsAsync(matchIds, cancellationToken)).ToDictionary(m => m.Id);
        var sheetMap = (await sheets.GetAvailableLinesByMatchIdsAsync(matchIds, cancellationToken)).ToDictionary(s => s.MatchId);
        var existing = (await proposals.GetByMatchesAsync(matchIds, cancellationToken)).ToDictionary(p => (p.SquadId, p.MatchId, p.SquadPlayerId));
        var names = (await teams.GetByIdsAsync(matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
            .ToDictionary(t => t.Id, t => t.Name);
        var bySeason = new Dictionary<Guid, IReadOnlyList<Squad>>();
        var squadInjuries = new Dictionary<Guid, IReadOnlyList<PlayerInjury>>();
        int created = 0, refreshed = 0;

        foreach (var group in mentions.GroupBy(m => m.MatchId))
        {
            if (!matchMap.TryGetValue(group.Key, out var match) || !sheetMap.TryGetValue(group.Key, out var sheet)) continue;
            if (!bySeason.TryGetValue(match.SeasonId, out var seasonSquads))
                bySeason[match.SeasonId] = seasonSquads = await squads.GetBySeasonAsync(match.SeasonId, cancellationToken);

            foreach (var side in group.GroupBy(m => m.Side))
            {
                var teamId = side.Key == MatchSide.Home ? match.HomeTeamId : match.AwayTeamId;
                foreach (var squad in seasonSquads.Where(s => s.TeamId == teamId))
                {
                    var byLine = SheetMatcher.Match(squad.ActivePlayers, sheet.LinesOf(side.Key))
                        .GroupBy(pair => pair.Value.NormalizedName)
                        .ToDictionary(g => g.Key, g => g.First().Key);
                    foreach (var mention in side)
                    {
                        if (!byLine.TryGetValue(mention.NormalizedName, out var squadPlayerId)) continue;
                        if (existing.TryGetValue((squad.Id, match.Id, squadPlayerId), out var proposal))
                        {
                            if (proposal.Refresh(mention)) refreshed++;
                            continue;
                        }

                        if (!squadInjuries.TryGetValue(squad.Id, out var current))
                            squadInjuries[squad.Id] = current = await injuries.GetBySquadAsync(squad.Id, cancellationToken);
                        if (AlreadyRecorded(current, squadPlayerId, match.Id, CallUps.DayOf(match.KickoffAt))) continue;

                        var player = squad.ActivePlayers.First(p => p.Id == squadPlayerId);
                        proposal = InjuryProposal.Create(squad.Id, squadPlayerId, mention, now);
                        proposals.Add(proposal);
                        existing[(squad.Id, match.Id, squadPlayerId)] = proposal;
                        created++;
                        await RaiseAsync(squad, player, match, proposal, names, cancellationToken);
                    }
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new InjuryProposalsSynced(mentions.Count, created, refreshed, expired);
    }

    private async Task<int> RetireAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = (await proposals.GetExpiredAsync(now - Keep, cancellationToken)).ToList();
        var pending = await proposals.GetPendingAsync(cancellationToken);
        if (pending.Count > 0)
        {
            var stored = (await reports.GetStoredByMatchesAsync(pending.Select(p => p.MatchId).Distinct().ToList(), cancellationToken))
                .Select(m => m.Id)
                .ToHashSet();
            expired.AddRange(pending.Where(p => !stored.Contains(p.MentionId) && expired.All(e => e.Id != p.Id)));
        }

        foreach (var group in expired.GroupBy(p => p.SquadId))
        {
            if (group.Any(p => p.IsPending) && await squads.GetByIdAsync(group.Key, cancellationToken) is { } squad)
            {
                foreach (var open in group.Where(p => p.IsPending))
                    await StaffAlerts.ResolveAsync(squad, clubs, alerts, InjuryProposalAccess.KeyFor(open.Id), clock, cancellationToken);
            }
            foreach (var proposal in group)
                proposals.Remove(proposal);
        }
        return expired.Count;
    }

    // Ya la apuntaron: dice que fue en ese partido, sigue de baja o tiene un parte de esos días.
    internal static bool AlreadyRecorded(IEnumerable<PlayerInjury> current, Guid squadPlayerId, Guid matchId, DateOnly matchDay) =>
        current.Any(i => i.SquadPlayerId == squadPlayerId
            && (i.MatchId == matchId || i.IsActive || i.StartedOn >= matchDay.AddDays(-1) && i.StartedOn <= matchDay.AddDays(7)));

    private async Task RaiseAsync(
        Squad squad, SquadPlayer player, Match match, InjuryProposal proposal, IReadOnlyDictionary<Guid, string> names, CancellationToken cancellationToken)
    {
        var fixture = $"{names.GetValueOrDefault(match.HomeTeamId, "?")} - {names.GetValueOrDefault(match.AwayTeamId, "?")}";
        var day = TimeZoneInfo.ConvertTime(match.KickoffAt, TrainingClock.Zone).ToString("dd/MM", Spanish);
        await StaffAlerts.RaiseAsync(squad, clubs, alerts, AlertKind.InjuryProposal, InjuryProposalAccess.KeyFor(proposal.Id),
            $"{player.FullName}: posible lesión",
            $"La crónica del partido {fixture} ({day}) cuenta que se lesionó: «{proposal.Phrase}». Regístrala o descártala en las lesiones de la plantilla.",
            $"/plantillas/{squad.Id}#lesiones", null, clock, cancellationToken);
    }
}

internal static class InjuryProposalAccess
{
    public const string KeyPrefix = "injury-proposal:";

    public static string KeyFor(Guid proposalId) => $"{KeyPrefix}{proposalId:N}";

    // Las propuestas son para quien registra las lesiones: el club y el cuerpo técnico. El administrador las ve, solo para leer.
    public static async Task<Squad> SquadForManagerOrAdminAsync(
        ISquadStore squads, IClubStore clubs, ICurrentUser currentUser, Guid squadId, TimeProvider clock, CancellationToken cancellationToken)
    {
        var squad = await InjuryAccess.SquadForViewerAsync(squads, clubs, currentUser, squadId, clock, cancellationToken);
        if (!currentUser.IsAdmin && !await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken))
            throw new ForbiddenException(ErrorCode.SquadManagersOnly);
        return squad;
    }

    public static async Task<InjuryProposal> PendingOfAsync(Squad squad, Guid proposalId, IInjuryProposalStore proposals, CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null || proposal.SquadId != squad.Id)
            throw new NotFoundException(ErrorCode.InjuryProposalNotFound, proposalId);
        if (!proposal.IsPending)
            throw new ConflictException(ErrorCode.InjuryProposalDecided);
        return proposal;
    }

    // Las pendientes de los jugadores que siguen en la plantilla, la más reciente primero.
    public static async Task<IReadOnlyList<InjuryProposalDto>> ListAsync(
        Squad squad, IInjuryProposalStore proposals, IMatchStore matches, ITeamStore teams, IInjuryTypeStore types, IBodySiteStore sites,
        CancellationToken cancellationToken)
    {
        var players = squad.ActivePlayers.ToDictionary(p => p.Id);
        var pending = (await proposals.GetBySquadAsync(squad.Id, cancellationToken))
            .Where(p => p.IsPending && players.ContainsKey(p.SquadPlayerId))
            .ToList();
        if (pending.Count == 0) return [];

        var matchMap = (await matches.GetByIdsAsync(pending.Select(p => p.MatchId).Distinct(), cancellationToken)).ToDictionary(m => m.Id);
        var names = (await teams.GetByIdsAsync(matchMap.Values.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct(), cancellationToken))
            .ToDictionary(t => t.Id, t => t.Name);
        var catalog = await types.GetAllAsync(cancellationToken);
        var anatomy = await sites.GetAllAsync(cancellationToken);

        return pending
            .Where(p => matchMap.ContainsKey(p.MatchId))
            .Select(p =>
            {
                var match = matchMap[p.MatchId];
                var suggested = InjuryTypeSuggester.Suggest($"{p.Phrase} {p.Excerpt}", catalog);
                var location = catalog.FirstOrDefault(t => t.Code == suggested) is { } type
                    ? InjuryLocationSuggester.Suggest(p.Phrase, type, anatomy)
                    : default;
                return new InjuryProposalDto(
                    p.Id, p.SquadPlayerId, players[p.SquadPlayerId].FullName, match.Id, match.Matchday, CallUps.DayOf(match.KickoffAt),
                    names.GetValueOrDefault(match.HomeTeamId, "?"), names.GetValueOrDefault(match.AwayTeamId, "?"),
                    p.Minute, p.Excerpt, p.Phrase, p.SourceUrl, suggested, location.Site?.Code, location.Side, p.DetectedAt);
            })
            .OrderByDescending(d => d.MatchDate)
            .ThenBy(d => d.PlayerName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

internal sealed class ListInjuryProposalsHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryProposalStore proposals, IMatchStore matches, ITeamStore teams,
    IInjuryTypeStore types, IBodySiteStore sites, TimeProvider clock) : IQueryHandler<ListInjuryProposalsQuery, IReadOnlyList<InjuryProposalDto>>
{
    public async Task<IReadOnlyList<InjuryProposalDto>> Handle(ListInjuryProposalsQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryProposalAccess.SquadForManagerOrAdminAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        return await InjuryProposalAccess.ListAsync(squad, proposals, matches, teams, types, sites, cancellationToken);
    }
}

internal sealed class DismissInjuryProposalHandler(
    ICurrentUser currentUser, ISquadStore squads, ICoachStore coaches, IClubStore clubs, IInjuryProposalStore proposals, IMatchStore matches,
    ITeamStore teams, IInjuryTypeStore types, IBodySiteStore sites, IAlertStore alerts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<DismissInjuryProposalCommand, IReadOnlyList<InjuryProposalDto>>
{
    public async Task<IReadOnlyList<InjuryProposalDto>> Handle(DismissInjuryProposalCommand command, CancellationToken cancellationToken)
    {
        var squad = await Resolvers.SquadForManagerAsync(squads, currentUser, clubs, command.SquadId, clock, cancellationToken);
        var proposal = await InjuryProposalAccess.PendingOfAsync(squad, command.ProposalId, proposals, cancellationToken);

        var editor = await Resolvers.CurrentEditorAsync(currentUser, coaches, clubs, cancellationToken);
        proposal.Dismiss(editor, clock.GetUtcNow());
        await StaffAlerts.ResolveAsync(squad, clubs, alerts, InjuryProposalAccess.KeyFor(proposal.Id), clock, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await InjuryProposalAccess.ListAsync(squad, proposals, matches, teams, types, sites, cancellationToken);
    }
}

// Las lesiones de un partido para sus sucesos: las que se hicieron en él (cada uno ve las que puede ver de las lesiones de
// la plantilla) y, al club, al cuerpo técnico y al administrador, también las posibles de la crónica de quien no tiene ya
// una de ese partido. Cada una con su línea del acta, buscada con el mismo cruce de nombres que el resto de la app, y el
// minuto: el de la crónica o, si no lo contó, el de cuando dejó el campo según el acta.
internal sealed class ListMatchInjuriesHandler(
    ICurrentUser currentUser, ISquadStore squads, IClubStore clubs, IInjuryProposalStore proposals, IInjuryStore injuries, IMatchStore matches,
    IOfficialSheetStore sheets, TimeProvider clock)
    : IQueryHandler<ListMatchInjuriesQuery, IReadOnlyList<MatchInjuryDto>>
{
    public async Task<IReadOnlyList<MatchInjuryDto>> Handle(ListMatchInjuriesQuery query, CancellationToken cancellationToken)
    {
        var squad = await InjuryAccess.SquadForViewerAsync(squads, clubs, currentUser, query.SquadId, clock, cancellationToken);
        var visible = await InjuryAccess.VisibleSquadPlayersAsync(squad, currentUser, clock, clubs, cancellationToken);
        var managers = currentUser.IsAdmin || await SquadAccess.IsManagerAsync(squad, currentUser, clock, clubs, cancellationToken);

        var linked = (await injuries.GetBySquadAsync(squad.Id, cancellationToken))
            .Where(i => i.MatchId == query.MatchId && visible(i.SquadPlayerId))
            .ToList();
        var found = (await proposals.GetByMatchesAsync([query.MatchId], cancellationToken))
            .Where(p => p.SquadId == squad.Id && visible(p.SquadPlayerId))
            .ToList();
        var pending = managers ? found.Where(p => p.IsPending && linked.All(i => i.SquadPlayerId != p.SquadPlayerId)).ToList() : [];
        if (linked.Count == 0 && pending.Count == 0) return [];

        var players = squad.Players.ToDictionary(p => p.Id);
        var lines = await LinesAsync(squad, query.MatchId, cancellationToken);
        var list = new List<MatchInjuryDto>();
        foreach (var injury in linked)
        {
            if (!players.TryGetValue(injury.SquadPlayerId, out var player)) continue;
            var proposal = found.FirstOrDefault(p => p.SquadPlayerId == player.Id);
            var line = lines.GetValueOrDefault(player.Id);
            list.Add(new MatchInjuryDto(
                player.Id, player.FullName, proposal?.Minute ?? line?.MinuteOut, injury.Reason ?? proposal?.Phrase ?? string.Empty, true, injury.Id,
                proposal?.Id, injury.TypeDefinition.Label, line?.Id, injury.Location));
        }
        foreach (var proposal in pending)
        {
            if (!players.TryGetValue(proposal.SquadPlayerId, out var player)) continue;
            list.Add(new MatchInjuryDto(
                player.Id, player.FullName, proposal.Minute, proposal.Phrase, false, null, proposal.Id, null, lines.GetValueOrDefault(player.Id)?.Id));
        }
        return list.OrderBy(i => i.Minute ?? int.MaxValue).ThenBy(i => i.PlayerName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, OfficialSheetLine>> LinesAsync(Squad squad, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await matches.GetByIdAsync(matchId, cancellationToken);
        MatchSide? side = match is null ? null : match.HomeTeamId == squad.TeamId ? MatchSide.Home : match.AwayTeamId == squad.TeamId ? MatchSide.Away : null;
        if (side is null) return new Dictionary<Guid, OfficialSheetLine>();

        var sheet = (await sheets.GetAvailableLinesByMatchIdsAsync([matchId], cancellationToken)).FirstOrDefault();
        if (sheet is null) return new Dictionary<Guid, OfficialSheetLine>();
        return SheetMatcher.Match(squad.Players, sheet.LinesOf(side.Value));
    }
}

// El tipo del catálogo que encaja con lo que cuenta la crónica. Primero los tipos de siempre por sus palabras, del más
// concreto al más general ("esguince" y "tobillo" antes que "esguince"); si no, uno creado por el administrador cuyo nombre
// sale en la crónica; si no, «Otra».
internal static class InjuryTypeSuggester
{
    private const string Other = "Other";

    private static readonly (string Code, Regex Pattern)[] Known =
    [
        ("CruciateLigament", Pattern(@"\bcruzado")),
        ("Meniscus", Pattern(@"\bmenisco")),
        ("StressFracture", Pattern(@"\bfractura (por|de) estres")),
        ("Fracture", Pattern(@"\b(fractur|fisur)\w*|\brotura (de|del|de la) (clavicula|tibia|perone|femur|costilla\w*|nariz|tabique|dedo|mano|muneca|brazo)|\brompio (el|la) (clavicula|tibia|perone|femur|nariz|tabique|dedo|mano|muneca|brazo)")),
        ("Dislocation", Pattern(@"\b(luxa\w*|luxo|subluxa\w*|disloc\w*)|\bse le salio (el|la) (hombro|codo|rotula|dedo)")),
        ("TendonRupture", Pattern(@"\brotura (del?|de la) tendon|\brotura\b.*\baquiles|\baquiles\b.*\brot")),
        ("MuscleTear", Pattern(@"\b(rotura (fibrilar|muscular|de fibras)|desgarro\w*|pinchazo\w*)")),
        ("MuscleStrain", Pattern(@"\b(elongacion\w*|distension\w*)")),
        ("MuscleOverload", Pattern(@"\bsobrecarga")),
        ("MuscleContracture", Pattern(@"\bcontractura\w*")),
        ("AnkleSprain", Pattern(@"\b(esguince|torcedura|torcio|torcerse|ligamento\w*)\b.*\btobillo|\btobillo\b.*\b(esguince|torcedura|torcio|torcerse)\b")),
        ("KneeSprain", Pattern(@"\besguince\b.*\brodilla|\brodilla\b.*\besguince\b|\bligamento (lateral|colateral)")),
        ("Sprain", Pattern(@"\besguince")),
        ("Concussion", Pattern(@"\bconmocion")),
        ("Laceration", Pattern(@"\b(herida|brecha|corte en|puntos de sutura)")),
        ("Tendinitis", Pattern(@"\b(tendinitis|tendinopatia)")),
        ("Periostitis", Pattern(@"\bperiostitis")),
        ("PlantarFasciitis", Pattern(@"\bfascitis")),
        ("Pubalgia", Pattern(@"\b(pubalgia|osteopatia (de|del) pubis)")),
        ("LowBackPain", Pattern(@"\b(lumbalgia|lumbago)")),
        ("Contusion", Pattern(@"\b(contusion|golpe|traumatismo)")),
    ];

    public static string? Suggest(string text, IReadOnlyList<InjuryTypeDefinition> types)
    {
        var active = types.Where(t => t.Active).ToList();
        var folded = InjuryMentionDetector.Fold(text);
        foreach (var (code, pattern) in Known)
        {
            if (pattern.IsMatch(folded) && active.FirstOrDefault(t => t.Code == code) is { } known)
                return known.Code;
        }

        foreach (var type in active.Where(t => t.Code != Other && Known.All(k => k.Code != t.Code)))
        {
            var root = InjuryMentionDetector.Fold(type.Label).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (root is { Length: >= 5 } && Regex.IsMatch(folded, $@"\b{Regex.Escape(root[..5])}"))
                return type.Code;
        }

        return active.FirstOrDefault(t => t.Code == Other)?.Code;
    }

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

// Dónde fue, según la frase de la crónica que lo cuenta (no la frase entera, donde un "lateral derecho" es una posición):
// el sitio que nombra entre los que pregunta el tipo, y el lado si dice uno solo. Gana el que encaja con todas sus palabras
// y, entre esos, el de más palabras ("quinto metatarsiano" antes que "metatarsianos"); si solo se nombra el principio
// ("el gemelo") y vale para varios del mismo grupo, el grupo. Si no se sabe, el sitio fijo del tipo o nada: precisión antes
// que cobertura, que lo confirma quien registra la lesión.
internal static class InjuryLocationSuggester
{
    private static readonly HashSet<string> StopWords = ["de", "del", "la", "las", "el", "los", "y", "o", "en", "con", "al"];
    // Palabras que encabezan muchos nombres y no dicen cuál es ("Tendón rotuliano" se nombra como "tendinitis rotuliana").
    private static readonly HashSet<string> Generic =
        ["tendon", "tendones", "ligamento", "ligamentos", "articulacion", "hueso", "huesos", "zona", "musculo", "musculos"];
    private static readonly string[] Endings = ["es", "os", "as", "s", "o", "a", "e"];
    private static readonly Regex Words = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Left = new(@"\bizquierd", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Right = new(@"\bderech", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static (BodySite? Site, BodySide? Side) Suggest(string phrase, InjuryTypeDefinition type, IReadOnlyList<BodySite> sites)
    {
        if (!type.AsksSite) return (null, null);
        var folded = InjuryMentionDetector.Fold(phrase);
        var tokens = Words.Matches(folded).Select(m => m.Value).ToList();
        // El sitio fijo es lo que queda si no se precisa: no compite con lo que lo precisa.
        var pool = sites.Where(s => s.Active && type.Allows(s) && s.Code != type.BaseSiteCode).ToList();
        var site = Named(tokens, pool) ?? type.BaseSite;
        if (site is null) return (null, null);

        var sided = InjuryLocation.SideSite(type, site);
        BodySide? side = sided is { Paired: true } ? (Left.IsMatch(folded), Right.IsMatch(folded)) switch
        {
            (true, false) => BodySide.Left,
            (false, true) => BodySide.Right,
            _ => null,
        } : null;
        return (site, side);
    }

    private static BodySite? Named(IReadOnlyList<string> tokens, IReadOnlyList<BodySite> pool)
    {
        if (tokens.Count == 0 || pool.Count == 0) return null;
        var scored = pool.Select(site => (Site: site, Score: Score(site.Label, tokens))).ToList();

        var full = scored.Where(s => s.Score.Full > 0).ToList();
        if (full.Count > 0)
        {
            var most = full.Max(s => s.Score.Full);
            return Pick(full.Where(s => s.Score.Full == most).Select(s => s.Site).ToList(), pool);
        }

        // Sin ninguno entero: los que tienen varias de sus palabras o la primera (la que dice qué es, no "interno").
        var partial = scored.Where(s => s.Score.Matched >= 2 || s.Score.FirstMatched).ToList();
        if (partial.Count == 0) return null;
        var best = partial.Max(s => s.Score.Matched);
        return Pick(partial.Where(s => s.Score.Matched == best).Select(s => s.Site).ToList(), pool);
    }

    // Uno solo, ese; varios del mismo grupo, el que lo precisa si es uno (y el grupo encaja también) o el grupo; si no, nada.
    private static BodySite? Pick(IReadOnlyList<BodySite> tied, IReadOnlyList<BodySite> pool)
    {
        if (tied.Count == 1) return tied[0];
        var inner = tied.Where(s => s.ParentCode is not null).ToList();
        var groups = tied.Select(s => s.ParentCode ?? s.Code).Distinct().ToList();
        if (groups.Count != 1) return null;
        if (inner.Count == 1) return inner[0];
        return pool.FirstOrDefault(s => s.Code == groups[0]);
    }

    private static (int Full, int Matched, bool FirstMatched) Score(string label, IReadOnlyList<string> tokens)
    {
        var folded = InjuryMentionDetector.Fold(label);
        var open = folded.IndexOf('(');
        var main = LabelWords(open < 0 ? folded : folded[..open]);
        var alternative = open < 0 ? [] : LabelWords(folded[(open + 1)..].TrimEnd(')'));

        var matched = main.Count(word => Matches(word, tokens));
        var full = main.Count > 0 && matched == main.Count ? main.Count : 0;
        if (alternative.Count > 0 && alternative.All(word => Matches(word, tokens)))
            full = Math.Max(full, alternative.Count);
        return (full, matched, main.Count > 0 && Matches(main[0], tokens));
    }

    private static List<string> LabelWords(string text)
    {
        var words = Words.Matches(text).Select(m => m.Value).Where(w => w.Length >= 3 && !StopWords.Contains(w)).ToList();
        var specific = words.Where(w => !Generic.Contains(w)).ToList();
        return specific.Count > 0 ? specific : words;
    }

    // Las cortas, enteras o en plural ("codo", "codos"); las largas, por su raíz sin la terminación ("rodill" para "rodillas")
    // o abreviadas en la crónica con al menos cinco letras ("isquio" por "isquiotibiales").
    private static bool Matches(string word, IReadOnlyList<string> tokens)
    {
        if (word.Length <= 5)
            return tokens.Any(t => t == word || t == word + "s" || t == word + "es" || (word.EndsWith('s') && t == word[..^1]));
        var stem = Stem(word);
        return tokens.Any(t => t.StartsWith(stem, StringComparison.Ordinal) || (t.Length >= 5 && word.StartsWith(t, StringComparison.Ordinal)));
    }

    private static string Stem(string word)
    {
        foreach (var ending in Endings)
        {
            if (word.EndsWith(ending, StringComparison.Ordinal) && word.Length - ending.Length >= 5)
                return word[..^ending.Length];
        }
        return word;
    }
}
