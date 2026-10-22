using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.OfficialSheets;
using TercerTiempo.Application.Abstractions.Teams;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Sync;
using TercerTiempo.Application.Teams.SyncProfiles;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Competitions;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.OfficialSheets;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Infrastructure.OfficialSheets.Asturfutbol;

namespace TercerTiempo.Application.Tests;

public class FederationDatabaseFirstTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Completed_sheet_is_never_due_even_years_later()
    {
        var (match, sheet) = CompletedSheet();
        Assert.False(OfficialSheetRefresh.IsDue(sheet, match, Now.AddYears(5)));
    }

    [Fact]
    public void Pending_sheet_is_checked_at_most_daily()
    {
        var match = Match.Schedule(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "123", null, Now);
        Assert.False(OfficialSheetRefresh.IsDue(sheet, match, Now.AddHours(23)));
        Assert.True(OfficialSheetRefresh.IsDue(sheet, match, Now.AddHours(24)));
    }

    [Fact]
    public async Task Reading_a_saved_sheet_never_queues_or_contacts_the_provider()
    {
        var (match, sheet) = CompletedSheet();
        var handler = new GetOfficialSheetHandler(
            Stub<IOfficialSheetProvider>((method, _) => throw new Exception($"Unexpected provider call: {method}")),
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<OfficialMatchSheet?>(sheet)),
            Stub<IOfficialSheetQueue>((method, _) => throw new Exception($"Unexpected queue call: {method}")),
            new OfficialSheetSyncMemory(), TimeProvider.System);
        var result = await handler.Handle(new(match.Id), default);
        Assert.Equal(OfficialSheetAvailability.Available, result.Status);
        Assert.NotEmpty(result.Home!.Starters);
    }

    [Fact]
    public async Task Even_a_legacy_force_command_cannot_download_an_existing_complete_sheet()
    {
        var (match, sheet) = CompletedSheet();
        var handler = new SyncOfficialSheetHandler(null!, null!,
            Stub<IMatchStore>((_, _) => Task.FromResult<Match?>(match)), null!,
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<OfficialMatchSheet?>(sheet)),
            null!, new OfficialSheetSyncMemory(), TimeProvider.System, NullLogger<SyncOfficialSheetHandler>.Instance);
        var result = await handler.Handle(new(match.Id, Force: true), default);
        Assert.Equal(OfficialSheetAvailability.Available, result.Status);
    }

    [Fact]
    public async Task Background_scan_with_all_data_saved_does_not_resolve_federation_catalogs()
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var (match, sheet) = CompletedSheet(season.Id);
        var handler = new PlanOfficialSheetSyncHandler(
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition])),
            Stub<IMatchStore>((_, _) => Task.FromResult<IReadOnlyList<Match>>([match])),
            Stub<IOfficialSheetStore>((_, _) => Task.FromResult<IReadOnlyList<OfficialMatchSheet>>([sheet])),
            new OfficialSheetSyncMemory(), TimeProvider.System);
        Assert.Empty(await handler.Handle(new(20), default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Profile_sync_preserves_saved_data_and_only_resumes_a_missing_crest(bool hasCrest)
    {
        var competition = Competition.Create("Liga", "Primera", CompetitionLevel.Regional, 1, null, "Asturias", null);
        var season = competition.AddSeason("2026/2027", new(2026, 8, 1), new(2027, 6, 30), null);
        competition.MarkCurrent(season.Id);
        var team = Team.Create("Equipo", "EQ", null);
        team.LinkOfficial(ExternalRef.Create("asturfutbol", "123"));
        team.UpdateProfile(new TeamProfile { OfficialName = "Ficha guardada" }, Now);
        if (hasCrest) team.SetCrest("existing.png");
        var profileCalls = 0;
        var provider = Stub<ITeamProfileProvider>((method, _) => method switch
        {
            "get_ProviderName" => "asturfutbol",
            "GetProfileAsync" => ReadProfile(),
            "DownloadCrestAsync" => Task.FromResult<CrestImage?>(new([1], "image/png")),
            _ => throw new Exception($"Unexpected provider call: {method}"),
        });
        Task<OfficialTeamProfile?> ReadProfile()
        {
            profileCalls++;
            return Task.FromResult<OfficialTeamProfile?>(new("https://federation.invalid/crest.png", new() { OfficialName = "Otra ficha" }));
        }
        var handler = new SyncTeamProfilesHandler(null!, provider,
            Stub<ICrestStorage>((_, _) => Task.FromResult("downloaded.png")), null!,
            Stub<ICompetitionStore>((_, _) => Task.FromResult<IReadOnlyList<Competition>>([competition])),
            Stub<ITeamStore>((_, _) => Task.FromResult<IReadOnlyList<Team>>([team])),
            Stub<IUnitOfWork>((_, _) => Task.FromResult(1)), Options.Create(new CrestOptions()),
            Stub<ISyncProgress>((_, _) => null), TimeProvider.System, NullLogger<SyncTeamProfilesHandler>.Instance);
        var result = await handler.Handle(new(Force: true), default);
        Assert.Empty(result.Errors);
        Assert.Equal(hasCrest ? 0 : 1, profileCalls);
        Assert.Equal(hasCrest ? "existing.png" : "downloaded.png", team.CrestKey);
        Assert.Equal("Ficha guardada", team.Profile.OfficialName);
    }

    private static (Match, OfficialMatchSheet) CompletedSheet(Guid? seasonId = null)
    {
        var match = Match.Schedule(seasonId ?? Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), null, null);
        match.RecordResult(2, 1, Now.AddDays(-2).AddHours(2));
        var sheet = OfficialMatchSheet.Create(match.Id, "asturfutbol", "123", null, Now.AddDays(-1));
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.acta-25744780.html")))!;
        using var reader = new StreamReader(stream);
        sheet.Apply(AsturfutbolPages.ParseSheet(reader.ReadToEnd(), 90)!, Now.AddDays(-1));
        return (match, sheet);
    }

    private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, PlayerInvitationTests.StoreProxy>();
        ((PlayerInvitationTests.StoreProxy)(object)result).Call = call;
        return result;
    }
}
