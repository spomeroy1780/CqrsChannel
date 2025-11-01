using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CqrsExpress.Contracts;

namespace CqrsExpress.Core.Aot;

/// <summary>
/// AOT-compatible handler registry implementation.
/// Uses only compile-time known types and delegates - no reflection or expression trees.
/// </summary>
public sealed class AotHandlerRegistry : IAotHandlerRegistry
{
    private readonly ConcurrentDictionary<(Type, Type), Func<object, object, CancellationToken, ValueTask<object>>> _queryHandlers = new();
    private readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, ValueTask>> _commandHandlers = new();
    private readonly ConcurrentDictionary<Type, List<Func<object, object, CancellationToken, ValueTask>>> _eventHandlers = new();

    /// <summary>
    /// Register a query handler with a strongly-typed invoker delegate.
    /// The invoker is created at compile-time with full type safety.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterQueryHandler<TRequest, TResponse>(
        Func<IQueryHandler<TRequest, TResponse>, TRequest, CancellationToken, ValueTask<TResponse>> invoker)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        var key = (typeof(TRequest), typeof(TResponse));
        
        // Create a type-erased wrapper that maintains the strongly-typed invoker
        _queryHandlers[key] = (handler, request, ct) =>
        {
            var typedHandler = (IQueryHandler<TRequest, TResponse>)handler;
            var typedRequest = (TRequest)request;
            return ConvertToObjectAsync(invoker(typedHandler, typedRequest, ct));
        };
    }

    /// <summary>
    /// Register a command handler with a strongly-typed invoker delegate.
    /// The invoker is created at compile-time with full type safety.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterCommandHandler<TCommand>(
        Func<ICommandHandler<TCommand>, TCommand, CancellationToken, ValueTask> invoker)
        where TCommand : ICommand
    {
        var key = typeof(TCommand);
        
        // Create a type-erased wrapper that maintains the strongly-typed invoker
        _commandHandlers[key] = (handler, command, ct) =>
        {
            var typedHandler = (ICommandHandler<TCommand>)handler;
            var typedCommand = (TCommand)command;
            return invoker(typedHandler, typedCommand, ct);
        };
    }

    /// <summary>
    /// Register an event handler with a strongly-typed invoker delegate.
    /// The invoker is created at compile-time with full type safety.
    /// Multiple handlers can be registered for the same event type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterEventHandler<TEvent>(
        Func<IEventHandler<TEvent>, TEvent, CancellationToken, ValueTask> invoker)
        where TEvent : IEvent
    {
        var key = typeof(TEvent);
        
        var invokerWrapper = (object handler, object evt, CancellationToken ct) =>
        {
            var typedHandler = (IEventHandler<TEvent>)handler;
            var typedEvent = (TEvent)evt;
            return invoker(typedHandler, typedEvent, ct);
        };

        _eventHandlers.AddOrUpdate(
            key,
            _ => new List<Func<object, object, CancellationToken, ValueTask>> { invokerWrapper },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(invokerWrapper);
                }
                return list;
            });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Func<object, object, CancellationToken, ValueTask<object>>? GetQueryHandlerInvoker(Type requestType, Type responseType)
    {
        return _queryHandlers.TryGetValue((requestType, responseType), out var invoker) ? invoker : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Func<object, object, CancellationToken, ValueTask>? GetCommandHandlerInvoker(Type commandType)
    {
        return _commandHandlers.TryGetValue(commandType, out var invoker) ? invoker : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<Func<object, object, CancellationToken, ValueTask>>? GetEventHandlerInvokers(Type eventType)
    {
        return _eventHandlers.TryGetValue(eventType, out var invokers) ? invokers : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<object> ConvertToObjectAsync<T>(ValueTask<T> valueTask)
    {
        T result = await valueTask.ConfigureAwait(false);
        return result!;
    }
}
