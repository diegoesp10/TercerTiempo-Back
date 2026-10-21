using System.Reflection;

namespace TercerTiempo.Domain.Common.Errors;

/// <summary>
/// Catálogo embebido de mensajes: el texto por defecto (español) de cada <see cref="ErrorCode"/>. Es la semilla de
/// la tabla ErrorMessages (migraciones) y el respaldo si la base de datos no tiene el código. Para añadir un idioma
/// basta con añadir sus filas aquí (o directamente en la tabla) con el mismo código.
/// </summary>
public static class ErrorCatalog
{
    private static readonly (string Code, string Message)[] Spanish =
    [
        (ErrorCode.TitleValidation, "Datos no válidos"),
        (ErrorCode.TitleDomain, "Regla de negocio incumplida"),
        (ErrorCode.TitleNotFound, "No encontrado"),
        (ErrorCode.TitleConflict, "Conflicto"),
        (ErrorCode.TitleForbidden, "Acceso denegado"),
        (ErrorCode.TitleUnauthenticated, "No autenticado"),
        (ErrorCode.TitleInvalidCredentials, "Credenciales incorrectas"),
        (ErrorCode.TitleBadRequest, "Petición no válida"),
        (ErrorCode.TitleInternal, "Error interno"),
        (ErrorCode.TitleTooManyRequests, "Demasiadas peticiones"),

        (ErrorCode.CommonRequired, "'{0}' no puede estar vacío."),
        (ErrorCode.CommonTooLong, "'{0}' no puede superar {1} caracteres."),
        (ErrorCode.CommonOutOfRange, "'{0}' debe estar entre {1} y {2}."),
        (ErrorCode.CommonNegative, "'{0}' no puede ser negativo."),
        (ErrorCode.CommonUnexpected, "Se ha producido un error inesperado."),
        (ErrorCode.ThrottleTooManyRequests, "Has superado el límite de {0} llamadas en {1} segundos: esta dirección queda bloqueada {2} minutos."),
        (ErrorCode.ThrottleBanned, "Esta dirección está bloqueada temporalmente por exceso de llamadas; vuelve a intentarlo en {0} minutos."),

        (ErrorCode.AuthUnauthenticated, "Debes iniciar sesión para realizar esta acción."),
        (ErrorCode.AuthInvalidCredentials, "Email o contraseña incorrectos."),
        (ErrorCode.AuthEmailTaken, "Ya existe una cuenta con ese email."),
        (ErrorCode.AuthPositionRequired, "Indica tu posición."),
        (ErrorCode.AuthAccessCodeRequired, "Introduce el código de acceso que te ha dado tu club."),
        (ErrorCode.AuthPasswordTooShort, "La contraseña debe tener al menos {0} caracteres."),
        (ErrorCode.AuthClubNotInCatalog, "Elige tu club de la lista: solo pueden darse de alta los clubes de las ligas que sigue Tercer Tiempo."),
        (ErrorCode.AuthClubAlreadyRegistered, "{0} ya tiene cuenta en Tercer Tiempo."),
        (ErrorCode.AuthPlayerAccountRequired, "Esta acción requiere una cuenta de jugador."),
        (ErrorCode.AuthCoachAccountRequired, "Esta acción requiere una cuenta del cuerpo técnico."),
        (ErrorCode.AuthClubAccountRequired, "Esta acción requiere una cuenta de club."),
        (ErrorCode.AccountNotFound, "La cuenta '{0}' no existe."),

        (ErrorCode.IdentityDuplicateUserName, "Ya existe una cuenta con ese email."),
        (ErrorCode.IdentityDuplicateEmail, "Ya existe una cuenta con ese email."),
        (ErrorCode.IdentityInvalidEmail, "El email no es válido."),
        (ErrorCode.IdentityInvalidUserName, "El email no es válido."),
        (ErrorCode.IdentityPasswordTooShort, "La contraseña debe tener al menos {0} caracteres."),
        (ErrorCode.IdentityPasswordRequiresNonAlphanumeric, "La contraseña debe incluir al menos un carácter que no sea letra ni número."),
        (ErrorCode.IdentityPasswordRequiresDigit, "La contraseña debe incluir al menos un número."),
        (ErrorCode.IdentityPasswordRequiresLower, "La contraseña debe incluir al menos una minúscula."),
        (ErrorCode.IdentityPasswordRequiresUpper, "La contraseña debe incluir al menos una mayúscula."),
        (ErrorCode.IdentityPasswordRequiresUniqueChars, "La contraseña debe tener al menos {0} caracteres distintos."),
        (ErrorCode.IdentityDefaultError, "No se ha podido crear la cuenta."),

        (ErrorCode.TeamNotFound, "El equipo '{0}' no existe."),
        (ErrorCode.TeamActiveSeasonNotFound, "El equipo '{0}' no tiene temporada activa."),
        (ErrorCode.TeamNoCurrentSeason, "La competición del equipo no tiene una temporada marcada como actual."),
        (ErrorCode.CompetitionNotFound, "La competición '{0}' no existe."),
        (ErrorCode.CompetitionNoCurrentSeason, "La competición no tiene una temporada en curso."),
        (ErrorCode.CompetitionNoCurrentSeasonNamed, "{0} no tiene temporada en curso."),
        (ErrorCode.CompetitionInvalidLevel, "Nivel de competición no válido."),
        (ErrorCode.CompetitionSeasonDates, "La temporada debe terminar después de empezar."),
        (ErrorCode.CompetitionSeasonExists, "La temporada '{0}' ya existe en esta competición."),
        (ErrorCode.CompetitionSeasonMismatch, "La temporada no pertenece a esta competición."),
        (ErrorCode.LeagueCompetitionRequired, "Indica la competición (competitionId) o vincúlate a una plantilla para ver la suya."),
        (ErrorCode.MatchdayNotFound, "La jornada {0} no existe."),
        (ErrorCode.MatchNotFound, "El partido '{0}' no existe."),
        (ErrorCode.MatchSameTeam, "Un equipo no puede jugar contra sí mismo."),
        (ErrorCode.MatchResultOnCancelled, "No se puede registrar el resultado de un partido cancelado."),
        (ErrorCode.MatchReschedulePlayed, "No se puede reprogramar un partido ya disputado."),
        (ErrorCode.MatchPostponePlayed, "No se puede aplazar un partido ya disputado."),
        (ErrorCode.MatchCancelPlayed, "No se puede cancelar un partido ya disputado."),
        (ErrorCode.MatchCannotStart, "El partido no puede pasar a 'en juego' desde su estado actual."),
        (ErrorCode.MatchTeamNotInvolved, "El equipo no participa en este partido."),
        (ErrorCode.MatchNotOfSquad, "El partido no es de esta plantilla."),
        (ErrorCode.MatchPreviewCoachOnly, "La previa del partido es para el cuerpo técnico."),
        (ErrorCode.OfficialSheetInvalidEvent, "Tipo de suceso no válido."),

        (ErrorCode.ClubTeamNotLinked, "Ese equipo no está vinculado a tu club."),
        (ErrorCode.ClubTeamAlreadyInClub, "Ese equipo ya forma parte del club."),
        (ErrorCode.ClubTeamNotInClub, "Ese equipo no forma parte del club."),
        (ErrorCode.ClubTeamNameMismatch, "{0} no se corresponde con el nombre del club ({1})."),
        (ErrorCode.ClubTeamLinkedElsewhere, "Ese equipo ya está vinculado a otro club."),
        (ErrorCode.ClubCrestOwnTeamsOnly, "Solo puedes cambiar el escudo de los equipos de tu club."),
        (ErrorCode.ClubCrestInvalid, "El escudo tiene que ser una imagen de hasta 2 MB."),
        (ErrorCode.ClubCrestTooLarge, "El escudo no puede superar los 2 MB."),
        (ErrorCode.ClubCrestFormat, "Formato no admitido: usa PNG, JPG, WebP, SVG o GIF."),
        (ErrorCode.ClubRosterOwnTeamsOnly, "Solo puedes editar la plantilla de los equipos de tu club."),
        (ErrorCode.RosterEntryNotFound, "La ficha '{0}' no existe."),
        (ErrorCode.RosterEntryDuplicateName, "Ya hay una ficha con ese nombre en la plantilla."),
        (ErrorCode.CrestDownloadFailed, "No se ha podido descargar una imagen de esa URL."),
        (ErrorCode.CrestInvalidUrl, "Indica una URL http(s) válida."),
        (ErrorCode.FileEmpty, "El archivo está vacío."),

        (ErrorCode.AppointmentNotFound, "El nombramiento '{0}' no existe."),
        (ErrorCode.AppointmentNotOfClub, "Ese nombramiento no es de tu club."),
        (ErrorCode.AppointmentBeyondSeason, "El nombramiento no puede ir más allá del final de la temporada ({0})."),
        (ErrorCode.AppointmentValidityPast, "La fecha de validez ya ha pasado."),
        (ErrorCode.AppointmentValidityOrder, "La validez del nombramiento no puede terminar antes de empezar."),
        (ErrorCode.AppointmentRevoked, "El club ha revocado este código de acceso."),
        (ErrorCode.AppointmentAlreadyRedeemed, "Este código de acceso ya se ha utilizado."),
        (ErrorCode.AppointmentExpired, "Este código de acceso ha caducado: pide al club uno nuevo."),
        (ErrorCode.AppointmentAlreadyRevoked, "El nombramiento ya estaba revocado."),
        (ErrorCode.AppointmentRevokedValidity, "No se puede cambiar la validez de un nombramiento revocado."),
        (ErrorCode.AppointmentCodeRegenerate, "El código ya se ha canjeado: no se puede regenerar."),
        (ErrorCode.AccessCodeNotFound, "El código de acceso '{0}' no existe."),
        (ErrorCode.StaffInvalidRole, "Rol del cuerpo técnico no válido."),

        (ErrorCode.SquadNotFound, "La plantilla '{0}' no existe."),
        (ErrorCode.SquadStaffOnly, "Solo el cuerpo técnico vigente de la plantilla puede hacer esto."),
        (ErrorCode.SquadPrivate, "Los datos de esta plantilla son privados: solo su cuerpo técnico y sus jugadores pueden verlos."),
        (ErrorCode.SquadAlreadyStaff, "Ya formas parte del cuerpo técnico de esta plantilla."),
        (ErrorCode.SquadStaffNotMember, "Ese técnico no forma parte de la plantilla."),
        (ErrorCode.SquadPlayerDuplicate, "'{0}' ya está en la plantilla."),
        (ErrorCode.SquadPlayerNotMember, "El jugador no pertenece a esta plantilla."),
        (ErrorCode.SquadPlayerAlreadyLeft, "El jugador ya había causado baja."),
        (ErrorCode.SquadEntryInactive, "Esa ficha ya no está activa en la plantilla."),
        (ErrorCode.SquadEntryLinked, "Esa ficha ya está vinculada a otro jugador registrado."),
        (ErrorCode.SquadAlreadyLinkedHere, "Ya estás vinculado a otra ficha de esta plantilla."),
        (ErrorCode.SquadNotLinked, "No estás vinculado a esta plantilla."),
        (ErrorCode.SquadInvalidVisibility, "Visibilidad no válida."),
        (ErrorCode.SquadPlayerNotFound, "El jugador '{0}' no existe en la plantilla."),

        (ErrorCode.PlayerInvalidPosition, "Posición no válida."),
        (ErrorCode.PlayerMinAge, "El jugador debe tener al menos 14 años."),
        (ErrorCode.PlayerShirtNumberRange, "El dorsal tiene que estar entre 1 y 99."),
        (ErrorCode.PlayerAlreadyInSquad, "Ya estás vinculado a una plantilla. Desvincúlate antes de unirte a otra."),
        (ErrorCode.PlayerNoMatchingEntry, "No hay ninguna ficha con tu nombre: elige la tuya en la lista o pide al cuerpo técnico que te añada."),
        (ErrorCode.PlayerNotInSquad, "No estás vinculado a ninguna plantilla."),
        (ErrorCode.InviteCodeNotFound, "El código de invitación '{0}' no existe."),

        (ErrorCode.SheetStaffOnly, "Solo el cuerpo técnico vigente de la plantilla puede registrar el acta."),
        (ErrorCode.SheetMatchOtherSeason, "El partido no pertenece a la temporada de esta plantilla."),
        (ErrorCode.SheetTeamNotInvolved, "El equipo de la plantilla no participa en este partido."),
        (ErrorCode.SheetMatchNotPlayed, "Solo se puede registrar el acta de partidos ya disputados."),
        (ErrorCode.SheetShotsOnTarget, "Los tiros a puerta no pueden superar los tiros totales."),
        (ErrorCode.SheetTwoYellows, "Dos amarillas implican expulsión: marca la tarjeta roja."),
        (ErrorCode.SheetNoMinutes, "Sin minutos jugados no se pueden registrar acciones de juego."),
        (ErrorCode.SheetNotesTooLong, "Las notas no pueden superar {0} caracteres."),
        (ErrorCode.SheetLineMismatch, "La línea del acta no corresponde a ese jugador o partido."),

        (ErrorCode.VideoNotFound, "El vídeo '{0}' no existe."),
        (ErrorCode.VideoFormatNotAllowed, "Formato no admitido. Sube un vídeo en {0}."),
        (ErrorCode.VideoTooLarge, "El vídeo no puede superar {0} MB."),
        (ErrorCode.VideoTooSmall, "El archivo está vacío o es demasiado pequeño para ser un vídeo."),
        (ErrorCode.VideoAlreadyDeleted, "El vídeo ya se había eliminado."),
        (ErrorCode.VideoOwnerAccess, "Este vídeo es de tu plantilla: ya puedes verlo."),
        (ErrorCode.VideoAlreadyPurchased, "Ya tienes acceso a este vídeo."),
        (ErrorCode.VideoPaymentFailed, "El pago no se ha completado."),
        (ErrorCode.VideoPaymentRejected, "El pago no se ha completado: {0}"),
        (ErrorCode.VideoTicketInvalid, "El ticket de reproducción no es válido o ha caducado."),
        (ErrorCode.VideoLocked, "Este vídeo es de pago: compra el acceso para verlo."),
        (ErrorCode.VideoNotAllowed, "Solo la plantilla que subió el vídeo y los técnicos que lo han comprado pueden verlo."),
    ];

    private static readonly Dictionary<string, string> Defaults = Spanish.ToDictionary(e => e.Code, e => e.Message);

    /// <summary>Filas de semilla de la tabla ErrorMessages (todos los idiomas embebidos).</summary>
    public static IReadOnlyList<ErrorMessage> Messages { get; } =
        Spanish.Select(e => new ErrorMessage(e.Code, ErrorMessage.DefaultLanguage, e.Message)).ToList();

    /// <summary>Todos los códigos declarados en <see cref="ErrorCode"/>.</summary>
    public static IReadOnlyList<string> Codes { get; } = typeof(ErrorCode)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    public static bool Contains(string code) => Defaults.ContainsKey(code);

    /// <summary>Plantilla en español del código; si no existe, el propio código (nunca se pierde el error).</summary>
    public static string Template(string code) => Defaults.GetValueOrDefault(code) ?? code;

    /// <summary>Mensaje en español con los argumentos aplicados.</summary>
    public static string Format(string code, params object[] args) => Fill(Template(code), args);

    /// <summary>Rellena los marcadores {0}, {1}... de una plantilla; si faltan argumentos, deja el texto tal cual.</summary>
    public static string Fill(string template, object[] args)
    {
        if (args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
