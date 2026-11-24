using TercerTiempo.Application.Abstractions.Messaging;
using TercerTiempo.Application.Clubs.ManageTeams;
using TercerTiempo.Domain.Squads;
using TercerTiempo.Application.OfficialSheets;
using TercerTiempo.Application.Squads;
using TercerTiempo.Application.Squads.Charts;
using TercerTiempo.Application.Squads.GetSquad;
using TercerTiempo.Application.Squads.Injuries;
using TercerTiempo.Application.Squads.ListMySquads;
using TercerTiempo.Application.Squads.ManagePlayers;
using TercerTiempo.Application.Squads.Matches;
using TercerTiempo.Application.Squads.MatchSheet;
using TercerTiempo.Application.Squads.Settings;
using TercerTiempo.Application.Squads.Stats;
using TercerTiempo.Application.Squads.Suggestions;
using TercerTiempo.Application.Squads.TeamStats;

namespace TercerTiempo.Repository.Squads;

internal sealed class SquadsRepository(
    IQueryHandler<ListMySquadsQuery, IReadOnlyList<SquadSummaryDto>> listMine,
    IQueryHandler<GetSquadQuery, SquadDetailDto> getSquad,
    ICommandHandler<AddSquadPlayerCommand, SquadPlayerDto> addPlayer,
    ICommandHandler<UpdateSquadPlayerCommand, SquadPlayerDto> updatePlayer,
    ICommandHandler<RemoveSquadPlayerCommand, SquadPlayerDto> removePlayer,
    ICommandHandler<ReinstateSquadPlayerCommand, SquadPlayerDto> reinstatePlayer,
    ICommandHandler<ImportRosterCommand, SquadDetailDto> importRoster,
    ICommandHandler<SetSquadVisibilityCommand, SquadDetailDto> setVisibility,
    ICommandHandler<SetSquadCallUpSizeCommand, SquadDetailDto> setCallUpSize,
    ICommandHandler<GeneratePlayerInviteCodeCommand, SquadDetailDto> generatePlayerInviteCode,
    IQueryHandler<ListSquadMatchesQuery, IReadOnlyList<SquadMatchDto>> listMatches,
    IQueryHandler<GetMatchSheetQuery, MatchSheetDto> getMatchSheet,
    ICommandHandler<RecordMatchSheetCommand, MatchSheetDto> recordMatchSheet,
    ICommandHandler<CompareMatchSheetCommand, SheetComparisonDto> compareMatchSheet,
    IQueryHandler<ListSquadSuggestionsQuery, IReadOnlyList<SuggestionDto>> listSuggestions,
    ICommandHandler<ResolveSuggestionCommand, IReadOnlyList<SuggestionDto>> resolveSuggestion,
    IQueryHandler<GetSquadStatsQuery, SquadStatsDto> getStats,
    IQueryHandler<GetSquadSeasonTableQuery, SquadSeasonTableDto> getSeasonTable,
    IQueryHandler<Application.Squads.Suspensions.GetSquadDisciplineQuery, Application.Squads.Suspensions.SquadDisciplineDto> getDiscipline,
    IQueryHandler<GetSquadPlayerProfileQuery, SquadPlayerProfileDto> getPlayerProfile,
    IQueryHandler<GetSquadTimelineQuery, SquadTimelineDto> getTimeline,
    IQueryHandler<ListInjuryTypesQuery, IReadOnlyList<InjuryTypeDto>> listInjuryTypes,
    IQueryHandler<ListSquadInjuriesQuery, IReadOnlyList<InjuryDto>> listInjuries,
    ICommandHandler<RecordInjuryCommand, IReadOnlyList<InjuryDto>> recordInjury,
    ICommandHandler<UpdateInjuryCommand, IReadOnlyList<InjuryDto>> updateInjury,
    ICommandHandler<CloseInjuryCommand, IReadOnlyList<InjuryDto>> closeInjury,
    ICommandHandler<ReopenInjuryCommand, IReadOnlyList<InjuryDto>> reopenInjury,
    ICommandHandler<RecordRelapseCommand, IReadOnlyList<InjuryDto>> recordRelapse,
    IQueryHandler<GetPlayerInjuryHistoryQuery, PlayerInjuryHistoryDto> getPlayerInjuryHistory,
    ICommandHandler<DeleteInjuryCommand, IReadOnlyList<InjuryDto>> deleteInjury,
    ICommandHandler<UploadInjuryDocumentCommand, IReadOnlyList<InjuryDto>> uploadInjuryDocument,
    ICommandHandler<DeleteInjuryDocumentCommand, IReadOnlyList<InjuryDto>> deleteInjuryDocument,
    IQueryHandler<OpenInjuryDocumentQuery, InjuryDocumentFile> openInjuryDocument,
    ICommandHandler<RecordTeamMatchStatsCommand, TeamMatchStatsDto> recordTeamStats,
    IQueryHandler<GetSquadTeamStatsQuery, SquadTeamStatsDto> getTeamStats) : ISquadsRepository
{
    public Task<IReadOnlyList<SquadSummaryDto>> ListMineAsync(CancellationToken cancellationToken = default) =>
        listMine.Handle(new ListMySquadsQuery(), cancellationToken);

    public Task<SquadDetailDto> GetAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getSquad.Handle(new GetSquadQuery(squadId), cancellationToken);

    public Task<SquadPlayerDto> AddPlayerAsync(Guid squadId, SquadPlayerRequest request, CancellationToken cancellationToken = default) =>
        addPlayer.Handle(request.ToAdd(squadId), cancellationToken);

    public Task<SquadPlayerDto> UpdatePlayerAsync(Guid squadId, Guid squadPlayerId, SquadPlayerRequest request, CancellationToken cancellationToken = default) =>
        updatePlayer.Handle(request.ToUpdate(squadId, squadPlayerId), cancellationToken);

    public Task<SquadPlayerDto> RemovePlayerAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        removePlayer.Handle(new RemoveSquadPlayerCommand(squadId, squadPlayerId), cancellationToken);

    public Task<SquadPlayerDto> ReinstatePlayerAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        reinstatePlayer.Handle(new ReinstateSquadPlayerCommand(squadId, squadPlayerId), cancellationToken);

    public Task<SquadDetailDto> ImportRosterAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        importRoster.Handle(new ImportRosterCommand(squadId), cancellationToken);

    public Task<SquadDetailDto> SetVisibilityAsync(Guid squadId, SquadVisibilityRequest request, CancellationToken cancellationToken = default) =>
        setVisibility.Handle(new SetSquadVisibilityCommand(squadId, request.Visibility), cancellationToken);

    public Task<SquadDetailDto> SetCallUpSizeAsync(Guid squadId, SquadCallUpSizeRequest request, CancellationToken cancellationToken = default) =>
        setCallUpSize.Handle(new SetSquadCallUpSizeCommand(squadId, request.Size), cancellationToken);

    public Task<SquadDetailDto> GeneratePlayerInviteCodeAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        generatePlayerInviteCode.Handle(new GeneratePlayerInviteCodeCommand(squadId, squadPlayerId), cancellationToken);

    public Task<IReadOnlyList<SquadMatchDto>> ListMatchesAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        listMatches.Handle(new ListSquadMatchesQuery(squadId), cancellationToken);

    public Task<MatchSheetDto> GetMatchSheetAsync(Guid squadId, Guid matchId, CancellationToken cancellationToken = default) =>
        getMatchSheet.Handle(new GetMatchSheetQuery(squadId, matchId), cancellationToken);

    public Task<MatchSheetDto> RecordMatchSheetAsync(Guid squadId, Guid matchId, MatchSheetRequest request, CancellationToken cancellationToken = default) =>
        recordMatchSheet.Handle(request.ToCommand(squadId, matchId), cancellationToken);

    public Task<SheetComparisonDto> CompareMatchSheetAsync(Guid squadId, Guid matchId, bool refresh, CancellationToken cancellationToken = default) =>
        compareMatchSheet.Handle(new CompareMatchSheetCommand(squadId, matchId, refresh), cancellationToken);

    public Task<IReadOnlyList<SuggestionDto>> ListSuggestionsAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        listSuggestions.Handle(new ListSquadSuggestionsQuery(squadId), cancellationToken);

    public Task<IReadOnlyList<SuggestionDto>> ResolveSuggestionAsync(Guid squadId, Guid suggestionId, ResolveSuggestionRequest request, CancellationToken cancellationToken = default) =>
        resolveSuggestion.Handle(request.ToCommand(squadId, suggestionId), cancellationToken);

    public Task<SquadSeasonTableDto> GetSeasonTableAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getSeasonTable.Handle(new GetSquadSeasonTableQuery(squadId), cancellationToken);

    public Task<Application.Squads.Suspensions.SquadDisciplineDto> GetDisciplineAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getDiscipline.Handle(new Application.Squads.Suspensions.GetSquadDisciplineQuery(squadId), cancellationToken);

    public Task<SquadStatsDto> GetStatsAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getStats.Handle(new GetSquadStatsQuery(squadId), cancellationToken);

    public Task<TeamMatchStatsDto> RecordTeamStatsAsync(Guid squadId, Guid matchId, TeamMatchStatsInput request, CancellationToken cancellationToken = default) =>
        recordTeamStats.Handle(new RecordTeamMatchStatsCommand(squadId, matchId, request), cancellationToken);

    public Task<SquadTeamStatsDto> GetTeamStatsAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getTeamStats.Handle(new GetSquadTeamStatsQuery(squadId), cancellationToken);

    public Task<SquadPlayerProfileDto> GetPlayerProfileAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        getPlayerProfile.Handle(new GetSquadPlayerProfileQuery(squadId, squadPlayerId), cancellationToken);

    public Task<SquadTimelineDto> GetTimelineAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        getTimeline.Handle(new GetSquadTimelineQuery(squadId), cancellationToken);

    public Task<IReadOnlyList<InjuryTypeDto>> ListInjuryTypesAsync(CancellationToken cancellationToken = default) =>
        listInjuryTypes.Handle(new ListInjuryTypesQuery(), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> ListInjuriesAsync(Guid squadId, CancellationToken cancellationToken = default) =>
        listInjuries.Handle(new ListSquadInjuriesQuery(squadId), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> RecordInjuryAsync(Guid squadId, InjuryRequest request, CancellationToken cancellationToken = default) =>
        recordInjury.Handle(request.ToRecord(squadId), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> UpdateInjuryAsync(Guid squadId, Guid injuryId, InjuryRequest request, CancellationToken cancellationToken = default) =>
        updateInjury.Handle(request.ToUpdate(squadId, injuryId), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> CloseInjuryAsync(Guid squadId, Guid injuryId, CloseInjuryRequest request, CancellationToken cancellationToken = default) =>
        closeInjury.Handle(new CloseInjuryCommand(squadId, injuryId, request.ReturnedOn), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> ReopenInjuryAsync(Guid squadId, Guid injuryId, CancellationToken cancellationToken = default) =>
        reopenInjury.Handle(new ReopenInjuryCommand(squadId, injuryId), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> RecordRelapseAsync(Guid squadId, Guid injuryId, RelapseRequest request, CancellationToken cancellationToken = default) =>
        recordRelapse.Handle(request.ToCommand(squadId, injuryId), cancellationToken);

    public Task<PlayerInjuryHistoryDto> GetPlayerInjuryHistoryAsync(Guid squadId, Guid squadPlayerId, CancellationToken cancellationToken = default) =>
        getPlayerInjuryHistory.Handle(new GetPlayerInjuryHistoryQuery(squadId, squadPlayerId), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> DeleteInjuryAsync(Guid squadId, Guid injuryId, CancellationToken cancellationToken = default) =>
        deleteInjury.Handle(new DeleteInjuryCommand(squadId, injuryId), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> UploadInjuryDocumentAsync(
        Guid squadId, Guid injuryId, InjuryDocumentKind kind, string? note, CrestUpload file, CancellationToken cancellationToken = default) =>
        uploadInjuryDocument.Handle(new UploadInjuryDocumentCommand(squadId, injuryId, kind, note, file), cancellationToken);

    public Task<IReadOnlyList<InjuryDto>> DeleteInjuryDocumentAsync(Guid squadId, Guid injuryId, Guid documentId, CancellationToken cancellationToken = default) =>
        deleteInjuryDocument.Handle(new DeleteInjuryDocumentCommand(squadId, injuryId, documentId), cancellationToken);

    public Task<InjuryDocumentFile> OpenInjuryDocumentAsync(Guid squadId, Guid injuryId, Guid documentId, CancellationToken cancellationToken = default) =>
        openInjuryDocument.Handle(new OpenInjuryDocumentQuery(squadId, injuryId, documentId), cancellationToken);
}
