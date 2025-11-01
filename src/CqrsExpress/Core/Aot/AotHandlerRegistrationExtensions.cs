using System.Runtime.CompilerServices;
using CqrsExpress.Contracts;

namespace CqrsExpress.Core.Aot;

/// <summary>
/// Extension methods for AOT-compatible handler registration.
/// These provide a fluent API for registering handlers without reflection.
/// </summary>
public static class AotHandlerRegistrationExtensions
{
    /// <summary>
    /// Register a query handler with compile-time type safety.
    /// The invoker delegate is created at compile-time with zero overhead.
    /// </summary>
    /// <example>
    /// registry.RegisterQuery&lt;GetUserQuery, UserDto&gt;((handler, query, ct) => handler.Handle(query, ct));
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IAotHandlerRegistry RegisterQuery<TRequest, TResponse>(
        this IAotHandlerRegistry registry,
        Func<IQueryHandler<TRequest, TResponse>, TRequest, CancellationToken, ValueTask<TResponse>>? invoker = null)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        // Default invoker directly calls Handle method
        invoker ??= static (handler, request, ct) => handler.Handle(request, ct)!;
        
        registry.RegisterQueryHandler(invoker);
        return registry;
    }

    /// <summary>
    /// Register a command handler with compile-time type safety.
    /// The invoker delegate is created at compile-time with zero overhead.
    /// </summary>
    /// <example>
    /// registry.RegisterCommand&lt;CreateUserCommand&gt;((handler, cmd, ct) => handler.Handle(cmd, ct));
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IAotHandlerRegistry RegisterCommand<TCommand>(
        this IAotHandlerRegistry registry,
        Func<ICommandHandler<TCommand>, TCommand, CancellationToken, ValueTask>? invoker = null)
        where TCommand : ICommand
    {
        // Default invoker directly calls Handle method
        invoker ??= static (handler, command, ct) => handler.Handle(command, ct);
        
        registry.RegisterCommandHandler(invoker);
        return registry;
    }

    /// <summary>
    /// Register an event handler with compile-time type safety.
    /// The invoker delegate is created at compile-time with zero overhead.
    /// Multiple handlers can be registered for the same event type.
    /// </summary>
    /// <example>
    /// registry.RegisterEvent&lt;UserCreatedEvent&gt;((handler, evt, ct) => handler.Handle(evt, ct));
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IAotHandlerRegistry RegisterEvent<TEvent>(
        this IAotHandlerRegistry registry,
        Func<IEventHandler<TEvent>, TEvent, CancellationToken, ValueTask> invoker)
        where TEvent : notnull, IEvent
    {
        // Default invoker directly calls Handle method
        invoker = static (handler, @event, ct) => handler.Handle(@event, ct);
        
        registry.RegisterEventHandler(invoker);
        return registry;
    }

    /// <summary>
    /// Fluent batch registration API for multiple handlers.
    /// </summary>
    public static IAotHandlerRegistry RegisterHandlers(
        this IAotHandlerRegistry registry,
        Action<IAotHandlerRegistry> configure)
    {
        configure(registry);
        return registry;
    }
}
