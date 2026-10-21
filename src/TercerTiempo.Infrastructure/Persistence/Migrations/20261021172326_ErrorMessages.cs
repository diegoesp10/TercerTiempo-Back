using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TercerTiempo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErrorMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErrorMessages",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorMessages", x => new { x.Code, x.Language });
                });

            migrationBuilder.InsertData(
                table: "ErrorMessages",
                columns: new[] { "Code", "Language", "Message" },
                values: new object[,]
                {
                    { "access_code.not_found", "es", "El código de acceso '{0}' no existe." },
                    { "account.not_found", "es", "La cuenta '{0}' no existe." },
                    { "appointment.already_redeemed", "es", "Este código de acceso ya se ha utilizado." },
                    { "appointment.already_revoked", "es", "El nombramiento ya estaba revocado." },
                    { "appointment.beyond_season", "es", "El nombramiento no puede ir más allá del final de la temporada ({0})." },
                    { "appointment.code_regenerate", "es", "El código ya se ha canjeado: no se puede regenerar." },
                    { "appointment.expired", "es", "Este código de acceso ha caducado: pide al club uno nuevo." },
                    { "appointment.not_found", "es", "El nombramiento '{0}' no existe." },
                    { "appointment.not_of_club", "es", "Ese nombramiento no es de tu club." },
                    { "appointment.revoked", "es", "El club ha revocado este código de acceso." },
                    { "appointment.revoked_validity", "es", "No se puede cambiar la validez de un nombramiento revocado." },
                    { "appointment.validity_order", "es", "La validez del nombramiento no puede terminar antes de empezar." },
                    { "appointment.validity_past", "es", "La fecha de validez ya ha pasado." },
                    { "auth.access_code_required", "es", "Introduce el código de acceso que te ha dado tu club." },
                    { "auth.club_account_required", "es", "Esta acción requiere una cuenta de club." },
                    { "auth.club_already_registered", "es", "{0} ya tiene cuenta en Tercer Tiempo." },
                    { "auth.club_not_in_catalog", "es", "Elige tu club de la lista: solo pueden darse de alta los clubes de las ligas que sigue Tercer Tiempo." },
                    { "auth.coach_account_required", "es", "Esta acción requiere una cuenta del cuerpo técnico." },
                    { "auth.email_taken", "es", "Ya existe una cuenta con ese email." },
                    { "auth.invalid_credentials", "es", "Email o contraseña incorrectos." },
                    { "auth.password_too_short", "es", "La contraseña debe tener al menos {0} caracteres." },
                    { "auth.player_account_required", "es", "Esta acción requiere una cuenta de jugador." },
                    { "auth.position_required", "es", "Indica tu posición." },
                    { "auth.unauthenticated", "es", "Debes iniciar sesión para realizar esta acción." },
                    { "club.crest_format", "es", "Formato no admitido: usa PNG, JPG, WebP, SVG o GIF." },
                    { "club.crest_invalid", "es", "El escudo tiene que ser una imagen de hasta 2 MB." },
                    { "club.crest_own_teams_only", "es", "Solo puedes cambiar el escudo de los equipos de tu club." },
                    { "club.crest_too_large", "es", "El escudo no puede superar los 2 MB." },
                    { "club.roster_own_teams_only", "es", "Solo puedes editar la plantilla de los equipos de tu club." },
                    { "club.team_already_in_club", "es", "Ese equipo ya forma parte del club." },
                    { "club.team_linked_elsewhere", "es", "Ese equipo ya está vinculado a otro club." },
                    { "club.team_name_mismatch", "es", "{0} no se corresponde con el nombre del club ({1})." },
                    { "club.team_not_in_club", "es", "Ese equipo no forma parte del club." },
                    { "club.team_not_linked", "es", "Ese equipo no está vinculado a tu club." },
                    { "common.negative", "es", "'{0}' no puede ser negativo." },
                    { "common.out_of_range", "es", "'{0}' debe estar entre {1} y {2}." },
                    { "common.required", "es", "'{0}' no puede estar vacío." },
                    { "common.too_long", "es", "'{0}' no puede superar {1} caracteres." },
                    { "common.unexpected", "es", "Se ha producido un error inesperado." },
                    { "competition.invalid_level", "es", "Nivel de competición no válido." },
                    { "competition.no_current_season", "es", "La competición no tiene una temporada en curso." },
                    { "competition.no_current_season_named", "es", "{0} no tiene temporada en curso." },
                    { "competition.not_found", "es", "La competición '{0}' no existe." },
                    { "competition.season_dates", "es", "La temporada debe terminar después de empezar." },
                    { "competition.season_exists", "es", "La temporada '{0}' ya existe en esta competición." },
                    { "competition.season_mismatch", "es", "La temporada no pertenece a esta competición." },
                    { "crest.download_failed", "es", "No se ha podido descargar una imagen de esa URL." },
                    { "crest.invalid_url", "es", "Indica una URL http(s) válida." },
                    { "file.empty", "es", "El archivo está vacío." },
                    { "identity.DefaultError", "es", "No se ha podido crear la cuenta." },
                    { "identity.DuplicateEmail", "es", "Ya existe una cuenta con ese email." },
                    { "identity.DuplicateUserName", "es", "Ya existe una cuenta con ese email." },
                    { "identity.InvalidEmail", "es", "El email no es válido." },
                    { "identity.InvalidUserName", "es", "El email no es válido." },
                    { "identity.PasswordRequiresDigit", "es", "La contraseña debe incluir al menos un número." },
                    { "identity.PasswordRequiresLower", "es", "La contraseña debe incluir al menos una minúscula." },
                    { "identity.PasswordRequiresNonAlphanumeric", "es", "La contraseña debe incluir al menos un carácter que no sea letra ni número." },
                    { "identity.PasswordRequiresUniqueChars", "es", "La contraseña debe tener al menos {0} caracteres distintos." },
                    { "identity.PasswordRequiresUpper", "es", "La contraseña debe incluir al menos una mayúscula." },
                    { "identity.PasswordTooShort", "es", "La contraseña debe tener al menos {0} caracteres." },
                    { "invite_code.not_found", "es", "El código de invitación '{0}' no existe." },
                    { "league.competition_required", "es", "Indica la competición (competitionId) o vincúlate a una plantilla para ver la suya." },
                    { "match.cancel_played", "es", "No se puede cancelar un partido ya disputado." },
                    { "match.cannot_start", "es", "El partido no puede pasar a 'en juego' desde su estado actual." },
                    { "match.not_found", "es", "El partido '{0}' no existe." },
                    { "match.not_of_squad", "es", "El partido no es de esta plantilla." },
                    { "match.postpone_played", "es", "No se puede aplazar un partido ya disputado." },
                    { "match.preview_coach_only", "es", "La previa del partido es para el cuerpo técnico." },
                    { "match.reschedule_played", "es", "No se puede reprogramar un partido ya disputado." },
                    { "match.result_on_cancelled", "es", "No se puede registrar el resultado de un partido cancelado." },
                    { "match.same_team", "es", "Un equipo no puede jugar contra sí mismo." },
                    { "match.team_not_involved", "es", "El equipo no participa en este partido." },
                    { "matchday.not_found", "es", "La jornada {0} no existe." },
                    { "official_sheet.invalid_event", "es", "Tipo de suceso no válido." },
                    { "player.already_in_squad", "es", "Ya estás vinculado a una plantilla. Desvincúlate antes de unirte a otra." },
                    { "player.invalid_position", "es", "Posición no válida." },
                    { "player.min_age", "es", "El jugador debe tener al menos 14 años." },
                    { "player.no_matching_entry", "es", "No hay ninguna ficha con tu nombre: elige la tuya en la lista o pide al cuerpo técnico que te añada." },
                    { "player.not_in_squad", "es", "No estás vinculado a ninguna plantilla." },
                    { "player.shirt_number_range", "es", "El dorsal tiene que estar entre 1 y 99." },
                    { "roster_entry.duplicate_name", "es", "Ya hay una ficha con ese nombre en la plantilla." },
                    { "roster_entry.not_found", "es", "La ficha '{0}' no existe." },
                    { "sheet.line_mismatch", "es", "La línea del acta no corresponde a ese jugador o partido." },
                    { "sheet.match_not_played", "es", "Solo se puede registrar el acta de partidos ya disputados." },
                    { "sheet.match_other_season", "es", "El partido no pertenece a la temporada de esta plantilla." },
                    { "sheet.no_minutes", "es", "Sin minutos jugados no se pueden registrar acciones de juego." },
                    { "sheet.notes_too_long", "es", "Las notas no pueden superar {0} caracteres." },
                    { "sheet.shots_on_target", "es", "Los tiros a puerta no pueden superar los tiros totales." },
                    { "sheet.staff_only", "es", "Solo el cuerpo técnico vigente de la plantilla puede registrar el acta." },
                    { "sheet.team_not_involved", "es", "El equipo de la plantilla no participa en este partido." },
                    { "sheet.two_yellows", "es", "Dos amarillas implican expulsión: marca la tarjeta roja." },
                    { "squad_player.not_found", "es", "El jugador '{0}' no existe en la plantilla." },
                    { "squad.already_linked_here", "es", "Ya estás vinculado a otra ficha de esta plantilla." },
                    { "squad.already_staff", "es", "Ya formas parte del cuerpo técnico de esta plantilla." },
                    { "squad.entry_inactive", "es", "Esa ficha ya no está activa en la plantilla." },
                    { "squad.entry_linked", "es", "Esa ficha ya está vinculada a otro jugador registrado." },
                    { "squad.invalid_visibility", "es", "Visibilidad no válida." },
                    { "squad.not_found", "es", "La plantilla '{0}' no existe." },
                    { "squad.not_linked", "es", "No estás vinculado a esta plantilla." },
                    { "squad.player_already_left", "es", "El jugador ya había causado baja." },
                    { "squad.player_duplicate", "es", "'{0}' ya está en la plantilla." },
                    { "squad.player_not_member", "es", "El jugador no pertenece a esta plantilla." },
                    { "squad.private", "es", "Los datos de esta plantilla son privados: solo su cuerpo técnico y sus jugadores pueden verlos." },
                    { "squad.staff_not_member", "es", "Ese técnico no forma parte de la plantilla." },
                    { "squad.staff_only", "es", "Solo el cuerpo técnico vigente de la plantilla puede hacer esto." },
                    { "staff.invalid_role", "es", "Rol del cuerpo técnico no válido." },
                    { "team.active_season_not_found", "es", "El equipo '{0}' no tiene temporada activa." },
                    { "team.no_current_season", "es", "La competición del equipo no tiene una temporada marcada como actual." },
                    { "team.not_found", "es", "El equipo '{0}' no existe." },
                    { "title.bad_request", "es", "Petición no válida" },
                    { "title.conflict", "es", "Conflicto" },
                    { "title.domain", "es", "Regla de negocio incumplida" },
                    { "title.forbidden", "es", "Acceso denegado" },
                    { "title.internal", "es", "Error interno" },
                    { "title.invalid_credentials", "es", "Credenciales incorrectas" },
                    { "title.not_found", "es", "No encontrado" },
                    { "title.unauthenticated", "es", "No autenticado" },
                    { "title.validation", "es", "Datos no válidos" },
                    { "video.already_deleted", "es", "El vídeo ya se había eliminado." },
                    { "video.already_purchased", "es", "Ya tienes acceso a este vídeo." },
                    { "video.format_not_allowed", "es", "Formato no admitido. Sube un vídeo en {0}." },
                    { "video.locked", "es", "Este vídeo es de pago: compra el acceso para verlo." },
                    { "video.not_allowed", "es", "Solo la plantilla que subió el vídeo y los técnicos que lo han comprado pueden verlo." },
                    { "video.not_found", "es", "El vídeo '{0}' no existe." },
                    { "video.owner_access", "es", "Este vídeo es de tu plantilla: ya puedes verlo." },
                    { "video.payment_failed", "es", "El pago no se ha completado." },
                    { "video.payment_rejected", "es", "El pago no se ha completado: {0}" },
                    { "video.ticket_invalid", "es", "El ticket de reproducción no es válido o ha caducado." },
                    { "video.too_large", "es", "El vídeo no puede superar {0} MB." },
                    { "video.too_small", "es", "El archivo está vacío o es demasiado pequeño para ser un vídeo." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrorMessages");
        }
    }
}
