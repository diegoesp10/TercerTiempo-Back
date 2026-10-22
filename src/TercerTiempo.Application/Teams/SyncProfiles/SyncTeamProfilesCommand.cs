using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Sync;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Teams;

namespace TercerTiempo.Application.Teams.SyncProfiles;

public sealed record SyncTeamProfilesCommand(
    [property: Description("Compatibilidad: no se fuerzan descargas de datos ya guardados.")]
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
    ISyncProgress progress,
    TimeProvider clock,
    ILogger<SyncTeamProfilesHandler> logger) : ICommandHandler<SyncTeamProfilesCommand, TeamProfileSyncResultDto>
{
    private const double MinSimilarity = 0.6;

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

        var all = await competitions.GetAllAsync(cancellationToken);
        progress.Start("fichas", all.Count);

        foreach (var (competition, index) in all.Select((c, i) => (c, i)))
        {
            if (competition.CurrentSeason is not { } season) continue;
            progress.Step(competition.Name, index);

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
                    var hasProfile = !team.Profile.IsEmpty;
                    if (hasProfile && team.CrestKey is not null || fresh && !hasProfile) continue;

                    var profile = await profiles.GetProfileAsync(official.ExternalId, cancellationToken);
                    if (profile is null)
                    {
                        team.MarkProfileChecked(now);
                        withoutProfile++;
                        continue;
                    }

                    if (!hasProfile)
                    {
                        team.UpdateProfile(profile.Profile, now);
                        updated++;
                    }
                    else team.MarkProfileChecked(now);

                    if (profile.CrestUrl is { } crestUrl && team.CrestKey is null
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
                await unitOfWork.SaveChangesAsync(cancellationToken);
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

        progress.Finish($"{updated} fichas y {downloaded} escudos de la federación", errors);

        return new TeamProfileSyncResultDto(profiles.ProviderName, linked, updated, downloaded, withoutProfile, errors);
    }

    private async Task<int> LinkTeamsAsync(Competition competition, Season season, IReadOnlyList<Team> seasonTeams, CancellationToken cancellationToken)
    {
        var pending = seasonTeams.Where(t => t.OfficialRef?.Provider != profiles.ProviderName
            && (t.Profile.IsEmpty || t.CrestKey is null)).ToList();
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
