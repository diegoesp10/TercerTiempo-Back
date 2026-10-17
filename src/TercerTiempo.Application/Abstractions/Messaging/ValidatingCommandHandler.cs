using FluentValidation;
using FluentValidation.Results;

namespace TercerTiempo.Application.Abstractions.Messaging;

/// <summary>
/// Decorador que ejecuta todos los validadores registrados para el comando antes de delegar en el handler real.
/// Se registra automáticamente en <see cref="DependencyInjection.AddApplication"/>.
/// </summary>
internal sealed class ValidatingCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(command, cancellationToken);
            if (!result.IsValid)
                failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await inner.Handle(command, cancellationToken);
    }
}
