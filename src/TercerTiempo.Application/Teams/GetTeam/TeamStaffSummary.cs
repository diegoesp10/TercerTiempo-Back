using System.ComponentModel;
using System.Globalization;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Teams.GetTeam;

public sealed record TeamStaffDto(
    [property: Description("Actas oficiales de la temporada que traen el cuerpo técnico del equipo.")]
    int Sheets,
    [property: Description("Quienes figuran en ellas (datos públicos de la federación), los actuales primero y del entrenador al encargado de material. A las cuentas con sesión se suman los nombrados por el club en la app que aún no figuran en ninguna.")]
    IReadOnlyList<TeamStaffMemberDto> Members);

public sealed record TeamStaffMemberDto(
    string Name,
    [property: Description("Función con la que más figura en las actas, como la escribe la federación («Entrenador», «2ºEntrenador», «DEL. Campo»…); null si solo está nombrado en la app.")]
    string? OfficialRoleLabel,
    OfficialStaffRole? OfficialRole,
    [property: Description("Función en el cuerpo técnico de la app si el club lo ha nombrado; null si no.")]
    StaffRole? Role,
    [property: Description("Actas de la temporada en las que figura.")]
    int Appearances,
    [property: Description("Día del último partido en cuya acta figura; null si no figura en ninguna.")]
    DateOnly? LastSeen,
    [property: Description("false si ya no figura en las últimas actas del equipo y no tiene un nombramiento vigente: estuvo antes en la temporada.")]
    bool Current,
    [property: Description("Su perfil en la app (solo a cuentas con sesión); null si no tiene.")]
    Guid? CoachId,
    [property: Description("true si ya tiene perfil en la app (su club lo ha nombrado y ha canjeado el código); false si de momento solo figura en las actas.")]
    bool HasProfile = false,
    [property: Description("Sus tarjetas de la temporada en las actas oficiales cerradas y cómo llega al próximo partido (apercibido o sancionado); null si solo está nombrado en la app.")]
    TercerTiempo.Application.Squads.Suspensions.StaffDisciplineDto? Discipline = null);

public sealed record SheetStaffEntry(Guid SheetId, DateTimeOffset KickoffAt, OfficialStaffRole Role, string RoleLabel, string FullName);

// Un nombramiento del club ya canjeado; Names con los nombres por los que se le puede reconocer en un acta (el del perfil y
// el que escribió el club), ya normalizados.
public sealed record AppStaffEntry(Guid CoachId, string Name, IReadOnlyList<string> Names, StaffRole Role, bool Active);

// El cuerpo técnico que se ve en la ficha de un equipo: el de las actas oficiales de la temporada, que es público. Quien
// tiene nombramiento en la app sale con el nombre de su perfil (las actas no llevan tildes) y, si sigue vigente, entre los
// actuales. Que tenga perfil se dice siempre (quien no lo tiene sale con el nombre del acta y «sin perfil»); el enlace, su
// función en la app y quienes aún no figuran en ningún acta, solo con includeProfiles/AppOnly.
public static class TeamStaffSummary
{
    // Quien no figura en ninguna de las últimas actas del equipo ya no está (los cuerpos técnicos cambian a mitad de
    // temporada); con tres, un delegado que falta a un partido sigue siendo de los actuales.
    private const int RecentSheets = 3;

    private static readonly HashSet<string> Particles = ["de", "del", "la", "las", "los", "y", "e", "da", "do"];

    public static TeamStaffDto Build(
        IReadOnlyList<SheetStaffEntry> rows, IReadOnlyList<AppStaffEntry> appointed, bool includeAppOnly, bool includeProfiles,
        TercerTiempo.Application.Squads.Suspensions.StaffDiscipline? discipline = null)
    {
        rows = rows.Where(r => !OfficialStaffRoles.NotAPerson(r.FullName)).ToList();
        var sheetDates = rows.GroupBy(r => r.SheetId).Select(g => g.First().KickoffAt).OrderByDescending(d => d).ToList();
        var recentFrom = sheetDates.Count == 0 ? DateTimeOffset.MaxValue : sheetDates[Math.Min(RecentSheets, sheetDates.Count) - 1];

        // La misma persona puede venir escrita de dos formas en actas distintas (con o sin el segundo apellido).
        var people = new List<(string Normalized, List<SheetStaffEntry> Rows)>();
        foreach (var row in rows.OrderByDescending(r => r.FullName.Length))
        {
            var normalized = NameNormalizer.Normalize(PersonName.FromSheet(row.FullName));
            var person = people.FirstOrDefault(p => p.Normalized == normalized);
            if (person.Rows is null) person = people.FirstOrDefault(p => PersonName.SamePerson(p.Normalized, normalized));
            if (person.Rows is null) people.Add(person = (normalized, []));
            person.Rows.Add(row);
        }

        var byCoach = appointed
            .GroupBy(a => a.CoachId)
            .Select(g => g.OrderByDescending(a => a.Active).First())
            .ToList();
        var matched = new HashSet<Guid>();
        var members = new List<TeamStaffMemberDto>();
        foreach (var (_, personRows) in people)
        {
            var app = byCoach
                .OrderByDescending(a => a.Active)
                .FirstOrDefault(a => personRows.Any(r => a.Names.Any(n => PersonName.SameAsSheet(r.FullName, n))));
            if (app is not null) matched.Add(app.CoachId);

            var label = personRows.GroupBy(r => r.RoleLabel).OrderByDescending(g => g.Select(r => r.SheetId).Distinct().Count())
                .ThenBy(g => g.Min(r => r.Role)).First().Key;
            var role = personRows.GroupBy(r => r.Role).OrderByDescending(g => g.Select(r => r.SheetId).Distinct().Count())
                .ThenBy(g => g.Key).First().Key;
            var last = personRows.Max(r => r.KickoffAt);

            members.Add(new TeamStaffMemberDto(
                app?.Name ?? Display(personRows[0].FullName),
                label,
                role,
                includeProfiles ? app?.Role : null,
                personRows.Select(r => r.SheetId).Distinct().Count(),
                SpainTime.Today(last),
                last >= recentFrom || app?.Active == true,
                includeProfiles ? app?.CoachId : null,
                app is not null,
                discipline?.ForSheetName(personRows[0].FullName)?.ToDto()));
        }

        if (includeAppOnly)
        {
            members.AddRange(byCoach
                .Where(a => a.Active && !matched.Contains(a.CoachId))
                .Select(a => new TeamStaffMemberDto(a.Name, null, null, a.Role, 0, null, true, includeProfiles ? a.CoachId : null, HasProfile: true)));
        }

        return new TeamStaffDto(
            sheetDates.Count,
            members
                .OrderByDescending(m => m.Current)
                .ThenBy(Order)
                .ThenByDescending(m => m.Appearances)
                .ThenBy(m => m.Name, StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true))
                .ToList());
    }

    private static int Order(TeamStaffMemberDto member) => member.OfficialRole is { } official
        ? (int)official
        : member.Role switch
        {
            StaffRole.HeadCoach => (int)OfficialStaffRole.HeadCoach,
            StaffRole.AssistantCoach => (int)OfficialStaffRole.AssistantCoach,
            StaffRole.Analyst => (int)OfficialStaffRole.FitnessCoach,
            StaffRole.Delegate => (int)OfficialStaffRole.TeamDelegate,
            StaffRole.Physio => (int)OfficialStaffRole.Physio,
            _ => (int)OfficialStaffRole.Other,
        };

    // «FERNANDEZ PEÑA, MIGUEL» → «Miguel Fernandez Peña»: las actas lo escriben en mayúsculas y casi siempre sin tildes,
    // que no se pueden adivinar sin riesgo de inventarse un apellido.
    public static string Display(string sheetFullName)
    {
        var name = PersonName.FromSheet(sheetFullName);
        if (name.Any(char.IsLower)) return name;

        var words = CultureInfo.GetCultureInfo("es-ES").TextInfo.ToTitleCase(name.ToLowerInvariant())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((w, i) => i > 0 && Particles.Contains(w.ToLowerInvariant()) ? w.ToLowerInvariant() : w));
    }
}
