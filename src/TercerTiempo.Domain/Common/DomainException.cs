namespace TercerTiempo.Domain.Common;

/// <summary>Violación de una regla de negocio. La API la traduce a un 400/409 con ProblemDetails.</summary>
public class DomainException(string message) : Exception(message);
