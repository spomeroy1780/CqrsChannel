namespace CqrsExpress.Contracts;

public interface ICommandHandler<TCommand>
    where TCommand : ICommand
{
    ValueTask Handle(TCommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    ValueTask<TResult> Handle(TCommand command, CancellationToken cancellationToken = default);
}
