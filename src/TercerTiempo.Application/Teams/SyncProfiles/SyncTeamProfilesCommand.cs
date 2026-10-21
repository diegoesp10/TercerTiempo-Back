using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.SyncProfiles;

/// <summary>
/// Descarga de la federación la ficha de cada equipo de la temporada en curso (sede, campo, equipación) y su escudo,
/// que se guarda en nuestro almacenamiento. Primero localiza el código de cada equipo en la federación a partir de
/// las primeras jornadas de su competición. Los equipos sin ficha en la federación (los de fuera de Asturias) se
/// quedan con el escudo que descargó la sincronización de datos o con el que importe el club.
/// </summary>
public sealed record SyncTeamProfilesCommand(
    [property: Description("Volver a descargar fichas y escudos aunque se hayan consultado hace poco.")]
    bool Force = false) : ICommand<TeamProfileSyncResultDto>;

public sealed record TeamProfileSyncResultDto(
    string Provider,
    int TeamsLinked,
    int ProfilesUpdated,
    int CrestsDownloaded,
    int TeamsWithoutProfile,
    IReadOnlyList<string> Errors);

internal sealed class SyncTeamProfilesHandler(
    IOfficialSheetProvider sheets,
    ITeamProfileProvider profiles,
    ICrestStorage crests,
    OfficialSheetLocator locator,
    ICompetitionStore competitions,
    ITeamStore teams,
    IUnitOfWork unitOfWork,
    IOptions<CrestOptions> options,
    TimeProvider clock,
    ILogger<SyncTeamProfilesHandler> logger) : ICommandHandler<SyncTeamProfilesCommand, TeamProfileSyncResultDto>
{
    /// <summary>Parecido mínimo entre nuestro nombre y el de la federación para dar por bueno el código del equipo.</summary>
    private const double MinSimilarity = 0.6;

    /// <summary>Jornadas que se leen para encontrar a todos los equipos (en una juegan todos salvo el que descansa).</summary>
    private const int MatchdaysToScan = 2;

    public async Task<TeamProfileSyncResultDto> Handle(SyncTeamProfilesCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var staleBefore = now.AddDays(-Math.Max(1, options.Value.RefreshDays));
        var linked = 0;
        var updated = 0;
        var downloaded = 0;
        var withoutProfile = 0;
        var errors = new List<string>();

        foreach (var competition in await competitions.GetAllAsync(cancellationToken))
        {
            if (competition.CurrentSeason is not { } season) continue;

            try
            {
                var seasonTeams = await teams.GetBySeasonAsync(season.Id, cancellationToken);
                linked += await LinkTeamsAsync(competition, season, seasonTeams, cancellationToken);

                foreach (var team in seasonTeams)
                {
                    if (team.OfficialRef is not { } official || official.Provider != profiles.ProviderName)
                    {
                        withoutProfile++;
                        continue;
                    }

                    var fresh = team.ProfileSyncedAt is { } at && at > staleBefore;
                    if (!command.Force && fresh) continue;

                    var profile = await profiles.GetProfileAsync(official.ExternalId, cancellationToken);
                    if (profile is null)
                    {
                        team.MarkProfileChecked(now);
                        withoutProfile++;
                        continue;
                    }

                    team.UpdateProfile(profile.Profile, now);
                    updated++;

                    if (profile.CrestUrl is { } crestUrl && (command.Force || team.CrestKey is null)
                        && await profiles.DownloadCrestAsync(crestUrl, cancellationToken) is { } image)
                    {
                        team.SetCrest(await crests.SaveAsync(team.Id, image, cancellationToken));
                        downloaded++;
                    }
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ProviderThrottledException ex)
            {
                // Bloqueo temporal: seguir pidiendo solo lo alarga. Lo hecho hasta aquí queda guardado; el resto, en la próxima pasada.
                logger.LogWarning("{Provider}: bloqueo temporal, se interrumpe la descarga de fichas ({Message})", profiles.ProviderName, ex.Message);
                errors.Add($"{competition.Name}: {ex.Message} La descarga se reanuda en la siguiente sincronización.");
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Provider}: error con las fichas de {Competition}", profiles.ProviderName, competition.Name);
                errors.Add($"{competition.Name}: {ex.Message}");
            }
        }

        return new TeamProfileSyncResultDto(profiles.ProviderName, linked, updated, downloaded, withoutProfile, errors);
    }

    /// <summary>Asigna el código de la federación a los equipos que aún no lo tienen, por parecido de nombre con los de las primeras jornadas.</summary>
    private async Task<int> LinkTeamsAsync(Competition competition, Season season, IReadOnlyList<Team> seasonTeams, CancellationToken cancellationToken)
    {
        var pending = seasonTeams.Where(t => t.OfficialRef?.Provider != profiles.ProviderName).ToList();
        if (pending.Count == 0) return 0;

        var competitionRef = await locator.ResolveCompetitionAsync(competition, season, cancellationToken);
        if (competitionRef is null) return 0;

        var official = new Dictionary<string, string>();
        for (var matchday = 1; matchday <= MatchdaysToScan; matchday++)
        {
            foreach (var fixture in await sheets.GetMatchdayAsync(competitionRef, matchday, cancellationToken))
            {
                if (fixture.HomeCode is { } home) official.TryAdd(home, fixture.HomeName);
                if (fixture.AwayCode is { } away) official.TryAdd(away, fixture.AwayName);
            }
        }

        var candidates = pending
            .SelectMany(team => official.Select(o => (Team: team, Code: o.Key, Score: TeamNameSimilarity.Score(team.Name, o.Value))))
            .Where(c => c.Score >= MinSimilarity)
            .OrderByDescending(c => c.Score);

        var usedTeams = new HashSet<Guid>();
        var usedCodes = new HashSet<string>();
        var linked = 0;
        foreach (var (team, code, _) in candidates)
        {
            if (!usedTeams.Add(team.Id) || !usedCodes.Add(code)) continue;
            team.LinkOfficial(ExternalRef.Create(profiles.ProviderName, code));
            linked++;
        }

        return linked;
    }
}
