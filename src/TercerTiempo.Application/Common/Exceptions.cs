using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Common;

/// <summary>Recurso inexistente (404). El código dice qué recurso y el primer argumento suele ser su clave.</summary>
public sealed class NotFoundException(string code, params object[] args) : CodedException(code, args);

/// <summary>Conflicto con el estado actual (409).</summary>
public sealed class ConflictException(string code, params object[] args) : CodedException(code, args);

/// <summary>Acción no permitida para esta cuenta (403).</summary>
public sealed class ForbiddenException(string code, params object[] args) : CodedException(code, args);

/// <summary>Hace falta iniciar sesión (401).</summary>
public sealed class UnauthenticatedException() : CodedException(ErrorCode.AuthUnauthenticated);

/// <summary>Email o contraseña incorrectos (401).</summary>
public sealed class InvalidCredentialsException() : CodedException(ErrorCode.AuthInvalidCredentials);
