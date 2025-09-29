namespace CqrsExpress.Contracts;

public interface IEventHandler<TEvent>
    where TEvent : IEvent
{
    ValueTask Handle(TEvent @event, CancellationToken cancellationToken = default);
}

public interface IEventHandler<TEvent, TResult>
    where TEvent : IEvent
{
    ValueTask<TResult> Handle(TEvent @event, CancellationToken cancellationToken = default);
}
