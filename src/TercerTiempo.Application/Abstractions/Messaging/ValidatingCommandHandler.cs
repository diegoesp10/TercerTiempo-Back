using FluentValidation;
using FluentValidation.Results;

namespace TercerTiempo.Application.Abstractions.Messaging;

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
