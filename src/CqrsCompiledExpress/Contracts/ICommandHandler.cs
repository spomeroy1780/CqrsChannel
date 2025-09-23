using CqrsCompiledExpress.Contracts;

namespace CqrsCompiledExpress.Contracts;

public interface ICommandHandler<TCommand>
    where TCommand : ICommand
{
    ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    ValueTask<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
