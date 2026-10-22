using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Application.Common;

public sealed class NotFoundException(string code, params object[] args) : CodedException(code, args);

public sealed class ConflictException(string code, params object[] args) : CodedException(code, args);

public sealed class ForbiddenException(string code, params object[] args) : CodedException(code, args);

public sealed class UnauthenticatedException() : CodedException(ErrorCode.AuthUnauthenticated);

public sealed class InvalidCredentialsException() : CodedException(ErrorCode.AuthInvalidCredentials);
