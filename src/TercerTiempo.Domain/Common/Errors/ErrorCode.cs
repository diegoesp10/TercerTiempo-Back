namespace TercerTiempo.Domain.Common.Errors;

public static class ErrorCode
{
    // Títulos de las respuestas ProblemDetails
    public const string TitleValidation = "title.validation";
    public const string TitleDomain = "title.domain";
    public const string TitleNotFound = "title.not_found";
    public const string TitleConflict = "title.conflict";
    public const string TitleForbidden = "title.forbidden";
    public const string TitleUnauthenticated = "title.unauthenticated";
    public const string TitleInvalidCredentials = "title.invalid_credentials";
    public const string TitleBadRequest = "title.bad_request";
    public const string TitleInternal = "title.internal";
    public const string TitleTooManyRequests = "title.too_many_requests";

    // Comunes (Guard y errores no controlados)
    public const string CommonRequired = "common.required";
    public const string CommonTooLong = "common.too_long";
    public const string CommonOutOfRange = "common.out_of_range";
    public const string CommonNegative = "common.negative";
    public const string CommonUnexpected = "common.unexpected";

    // Límite de llamadas (por IP y por endpoint) y bloqueos
    public const string ThrottleTooManyRequests = "throttle.too_many_requests";
    public const string ThrottleBanned = "throttle.banned";

    // Cuentas y acceso
    public const string AuthUnauthenticated = "auth.unauthenticated";
    public const string AuthInvalidCredentials = "auth.invalid_credentials";
    public const string AuthEmailTaken = "auth.email_taken";
    public const string AuthPositionRequired = "auth.position_required";
    public const string AuthAccessCodeRequired = "auth.access_code_required";
    public const string AuthInviteCodeRequired = "auth.invite_code_required";
    public const string AuthPasswordTooShort = "auth.password_too_short";
    public const string AuthClubNotInCatalog = "auth.club_not_in_catalog";
    public const string AuthClubAlreadyRegistered = "auth.club_already_registered";
    public const string AuthPlayerAccountRequired = "auth.player_account_required";
    public const string AuthCoachAccountRequired = "auth.coach_account_required";
    public const string AuthClubAccountRequired = "auth.club_account_required";
    public const string AccountNotFound = "account.not_found";

    // Identity (reglas de contraseña y email de ASP.NET Identity)
    public const string IdentityDuplicateUserName = "identity.DuplicateUserName";
    public const string IdentityDuplicateEmail = "identity.DuplicateEmail";
    public const string IdentityInvalidEmail = "identity.InvalidEmail";
    public const string IdentityInvalidUserName = "identity.InvalidUserName";
    public const string IdentityPasswordTooShort = "identity.PasswordTooShort";
    public const string IdentityPasswordRequiresNonAlphanumeric = "identity.PasswordRequiresNonAlphanumeric";
    public const string IdentityPasswordRequiresDigit = "identity.PasswordRequiresDigit";
    public const string IdentityPasswordRequiresLower = "identity.PasswordRequiresLower";
    public const string IdentityPasswordRequiresUpper = "identity.PasswordRequiresUpper";
    public const string IdentityPasswordRequiresUniqueChars = "identity.PasswordRequiresUniqueChars";
    public const string IdentityDefaultError = "identity.DefaultError";

    // Equipos, competiciones, jornadas y partidos
    public const string TeamNotFound = "team.not_found";
    public const string TeamActiveSeasonNotFound = "team.active_season_not_found";
    public const string TeamNoCurrentSeason = "team.no_current_season";
    public const string CompetitionNotFound = "competition.not_found";
    public const string CompetitionNoCurrentSeason = "competition.no_current_season";
    public const string CompetitionNoCurrentSeasonNamed = "competition.no_current_season_named";
    public const string CompetitionInvalidLevel = "competition.invalid_level";
    public const string CompetitionSeasonDates = "competition.season_dates";
    public const string CompetitionSeasonExists = "competition.season_exists";
    public const string CompetitionSeasonMismatch = "competition.season_mismatch";
    public const string LeagueCompetitionRequired = "league.competition_required";
    public const string MatchdayNotFound = "matchday.not_found";
    public const string MatchNotFound = "match.not_found";
    public const string MatchSameTeam = "match.same_team";
    public const string MatchResultOnCancelled = "match.result_on_cancelled";
    public const string MatchReschedulePlayed = "match.reschedule_played";
    public const string MatchPostponePlayed = "match.postpone_played";
    public const string MatchCancelPlayed = "match.cancel_played";
    public const string MatchCannotStart = "match.cannot_start";
    public const string MatchTeamNotInvolved = "match.team_not_involved";
    public const string MatchNotOfSquad = "match.not_of_squad";
    public const string MatchPreviewStaffOnly = "match.preview_coach_only";
    public const string OfficialSheetInvalidEvent = "official_sheet.invalid_event";

    // Clubes, escudos y plantilla publicada
    public const string ClubTeamNotLinked = "club.team_not_linked";
    public const string ClubTeamAlreadyInClub = "club.team_already_in_club";
    public const string ClubTeamNotInClub = "club.team_not_in_club";
    public const string ClubTeamNameMismatch = "club.team_name_mismatch";
    public const string ClubTeamLinkedElsewhere = "club.team_linked_elsewhere";
    public const string ClubCrestOwnTeamsOnly = "club.crest_own_teams_only";
    public const string ClubCrestInvalid = "club.crest_invalid";
    public const string ClubCrestTooLarge = "club.crest_too_large";
    public const string ClubCrestFormat = "club.crest_format";
    public const string ClubRosterOwnTeamsOnly = "club.roster_own_teams_only";
    public const string RosterEntryNotFound = "roster_entry.not_found";
    public const string RosterEntryDuplicateName = "roster_entry.duplicate_name";
    public const string RosterPhotoInvalid = "roster_entry.photo_invalid";
    public const string RosterPhotoTooLarge = "roster_entry.photo_too_large";
    public const string RosterPhotoFormat = "roster_entry.photo_format";
    public const string CrestDownloadFailed = "crest.download_failed";
    public const string CrestInvalidUrl = "crest.invalid_url";
    public const string FileEmpty = "file.empty";

    // Nombramientos y códigos de acceso
    public const string AppointmentNotFound = "appointment.not_found";
    public const string AppointmentNotOfClub = "appointment.not_of_club";
    public const string AppointmentBeyondSeason = "appointment.beyond_season";
    public const string AppointmentValidityPast = "appointment.validity_past";
    public const string AppointmentValidityOrder = "appointment.validity_order";
    public const string AppointmentRevoked = "appointment.revoked";
    public const string AppointmentAlreadyRedeemed = "appointment.already_redeemed";
    public const string AppointmentExpired = "appointment.expired";
    public const string AppointmentAlreadyRevoked = "appointment.already_revoked";
    public const string AppointmentRevokedValidity = "appointment.revoked_validity";
    public const string AppointmentCodeRegenerate = "appointment.code_regenerate";
    public const string AccessCodeNotFound = "access_code.not_found";
    public const string StaffInvalidRole = "staff.invalid_role";

    // Plantillas internas
    public const string SquadNotFound = "squad.not_found";
    public const string SquadStaffOnly = "squad.staff_only";
    public const string SquadPrivate = "squad.private";
    public const string SquadAlreadyStaff = "squad.already_staff";
    public const string SquadStaffNotMember = "squad.staff_not_member";
    public const string SquadPlayerDuplicate = "squad.player_duplicate";
    public const string SquadPlayerNotMember = "squad.player_not_member";
    public const string SquadPlayerAlreadyLeft = "squad.player_already_left";
    public const string SquadEntryInactive = "squad.entry_inactive";
    public const string SquadEntryLinked = "squad.entry_linked";
    public const string SquadAlreadyLinkedHere = "squad.already_linked_here";
    public const string SquadNotLinked = "squad.not_linked";
    public const string SquadInvalidVisibility = "squad.invalid_visibility";
    public const string SquadPlayerNotFound = "squad_player.not_found";
    public const string InjuryNotFound = "injury.not_found";
    public const string InjuryAlreadyActive = "injury.already_active";
    public const string InjuryAlreadyClosed = "injury.already_closed";
    public const string InjuryDatesInvalid = "injury.dates_invalid";
    public const string InjuryReturnInvalid = "injury.return_invalid";
    public const string InjuryInvalidType = "injury.invalid_type";

    // Jugadores
    public const string PlayerInvalidPosition = "player.invalid_position";
    public const string PlayerMinAge = "player.min_age";
    public const string PlayerShirtNumberRange = "player.shirt_number_range";
    public const string PlayerAlreadyInSquad = "player.already_in_squad";
    public const string PlayerNoMatchingEntry = "player.no_matching_entry";
    public const string PlayerNotInSquad = "player.not_in_squad";
    public const string InviteCodeNotFound = "invite_code.not_found";
    public const string PlayerInviteChanged = "player.invite_changed";
    public const string SquadManagersOnly = "squad.managers_only";
    public const string VideoManagerRequired = "video.manager_required";

    // Acta interna
    public const string SheetStaffOnly = "sheet.staff_only";
    public const string SheetMatchOtherSeason = "sheet.match_other_season";
    public const string SheetTeamNotInvolved = "sheet.team_not_involved";
    public const string SheetMatchNotPlayed = "sheet.match_not_played";
    public const string SheetShotsOnTarget = "sheet.shots_on_target";
    public const string SheetPassesCompleted = "sheet.passes_completed";
    public const string TeamStatsSetPieceGoals = "team_stats.set_piece_goals";
    public const string SheetTwoYellows = "sheet.two_yellows";
    public const string SheetNoMinutes = "sheet.no_minutes";
    public const string SheetNotesTooLong = "sheet.notes_too_long";
    public const string SheetLineMismatch = "sheet.line_mismatch";
    public const string SuggestionNotFound = "suggestion.not_found";
    public const string PlayerPhotoTooLarge = "player.photo_too_large";
    public const string SuggestionAlreadyResolved = "suggestion.already_resolved";
    public const string SuggestionPendingExists = "suggestion.pending_exists";
    public const string SuggestionMessageTooLong = "suggestion.message_too_long";
    public const string SuggestionNoStaffToRecord = "suggestion.no_staff_to_record";
    public const string LicenseRequired = "license.required";
    public const string LicenseViewerRequired = "license.viewer_required";
    public const string LicenseNotFound = "license.not_found";
    public const string LicenseMonthsInvalid = "license.months_invalid";
    public const string LicensePaymentNotFound = "license.payment_not_found";
    public const string LicensePaymentAlreadyPaid = "license.payment_already_paid";
    public const string LicensePaymentCancelled = "license.payment_cancelled";
    public const string LicensePaymentNotConfirmed = "license.payment_not_confirmed";

    // Vídeos
    public const string VideoNotFound = "video.not_found";
    public const string VideoFormatNotAllowed = "video.format_not_allowed";
    public const string VideoTooLarge = "video.too_large";
    public const string VideoTooSmall = "video.too_small";
    public const string VideoAlreadyDeleted = "video.already_deleted";
    public const string VideoOwnerAccess = "video.owner_access";
    public const string VideoAlreadyPurchased = "video.already_purchased";
    public const string VideoPaymentFailed = "video.payment_failed";
    public const string VideoPaymentRejected = "video.payment_rejected";
    public const string VideoTicketInvalid = "video.ticket_invalid";
    public const string VideoPartInvalid = "video.part_invalid";
    public const string VideoVisibilityInvalid = "video.visibility_invalid";
    public const string VideoShareInvalid = "video.share_invalid";
    public const string VideoShareNotFound = "video.share_not_found";
    public const string VideoShareHoursRange = "video.share_hours_range";
    public const string VideoPartTaken = "video.part_taken";
    public const string VideoPartMix = "video.part_mix";
    public const string VideoUploadNotFound = "video_upload.not_found";
    public const string VideoUploadExpired = "video_upload.expired";
    public const string VideoUploadChunkOutOfOrder = "video_upload.chunk_out_of_order";
    public const string VideoUploadChunkSize = "video_upload.chunk_size";
    public const string VideoUploadIncomplete = "video_upload.incomplete";
    public const string VideoLocked = "video.locked";
    public const string VideoNotAllowed = "video.not_allowed";
}
