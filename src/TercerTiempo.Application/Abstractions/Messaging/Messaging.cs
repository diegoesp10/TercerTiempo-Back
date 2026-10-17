namespace TercerTiempo.Application.Abstractions.Messaging;

/// <summary>Intención de cambiar el estado del sistema. Se valida (FluentValidation) antes de ejecutarse.</summary>
public interface ICommand<TResult> { }

/// <summary>Lectura sin efectos secundarios.</summary>
public interface IQuery<TResult> { }

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
