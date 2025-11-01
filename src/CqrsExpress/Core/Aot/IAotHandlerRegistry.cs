using CqrsExpress.Contracts;

namespace CqrsExpress.Core.Aot;

/// <summary>
/// AOT-compatible handler registry interface.
/// Handlers must be registered at compile-time through source generation or manual registration.
/// </summary>
public interface IAotHandlerRegistry
{
    /// <summary>
    /// Register a query handler with compile-time known types.
    /// </summary>
    void RegisterQueryHandler<TRequest, TResponse>(Func<IQueryHandler<TRequest, TResponse>, TRequest, CancellationToken, ValueTask<TResponse>> invoker)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull;

    /// <summary>
    /// Register a command handler with compile-time known types.
    /// </summary>
    void RegisterCommandHandler<TCommand>(Func<ICommandHandler<TCommand>, TCommand, CancellationToken, ValueTask> invoker)
        where TCommand : ICommand;

    /// <summary>
    /// Register an event handler with compile-time known types.
    /// </summary>
    void RegisterEventHandler<TEvent>(Func<IEventHandler<TEvent>, TEvent, CancellationToken, ValueTask> invoker)
        where TEvent : IEvent;

    /// <summary>
    /// Get a query handler invoker for the specified types.
    /// </summary>
    Func<object, object, CancellationToken, ValueTask<object>>? GetQueryHandlerInvoker(Type requestType, Type responseType);

    /// <summary>
    /// Get a command handler invoker for the specified type.
    /// </summary>
    Func<object, object, CancellationToken, ValueTask>? GetCommandHandlerInvoker(Type commandType);

    /// <summary>
    /// Get event handler invokers for the specified type.
    /// </summary>
    IReadOnlyList<Func<object, object, CancellationToken, ValueTask>>? GetEventHandlerInvokers(Type eventType);
}
