using CqrsCompiledExpress.Contracts;

namespace CqrsCompiledExpress.Contracts;

public interface IEventHandler<TEvent>
    where TEvent : IEvent
{
    ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

public interface IEventHandler<TEvent, TResult>
    where TEvent : IEvent
{
    ValueTask<TResult> HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
