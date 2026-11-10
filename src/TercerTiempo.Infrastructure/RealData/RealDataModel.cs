using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Referees;
using TercerTiempo.Domain.Standings;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.Jobs;
using TercerTiempo.Infrastructure.OfficialSheets;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Infrastructure.RealData;

// Qué vive en la base de datos reales (tercertiempo_DatosReales), la que comparten todos los entornos:
//   Copied      lo público de las webs; cada entorno tiene una copia en su base (RealDataCopier), con los mismos Id.
//   SharedOnly  el estado de las descargas (peticiones, sesión de la federación, cola de actas, ajustes de la pasada de
//               ligas, escudos descargados): solo existe en la base real.
//   JobRun      cada base guarda su historial: la real, el de las descargas; la de cada entorno, el de sus trabajos.
// Lo demás (usuarios, clubes, plantillas internas, actas internas…) es de cada entorno y nunca llega a la base real.
internal static class RealDataModel
{
    // En orden de dependencias: cada tabla después de aquellas a las que apunta.
    public static readonly Type[] Copied =
    [
        typeof(SeasonYear), typeof(Competition), typeof(Season), typeof(Team), typeof(RosterEntry), typeof(Match),
        typeof(Standing), typeof(SeasonZone), typeof(Referee), typeof(MatchOfficial), typeof(OfficialMatchSheet),
        typeof(OfficialSheetLine), typeof(OfficialSheetEvent),
    ];

    public static readonly Type[] SharedOnly =
    [
        typeof(FederationHttpCache), typeof(FederationRequestState), typeof(FootballSyncSettings), typeof(MatchOfficialLookup),
        typeof(ScrapeRequest), typeof(SheetDownload), typeof(TeamCrestImage), typeof(RealDataIngester),
    ];

    public static readonly HashSet<Type> Stored = [.. Copied, .. SharedOnly, typeof(JobRun)];
}
