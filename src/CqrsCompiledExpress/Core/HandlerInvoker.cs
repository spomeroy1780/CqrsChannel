using System.Linq.Expressions;
using CqrsCompiledExpress.Contracts;

namespace CqrsCompiledExpress.Core;

/// <summary>
/// Compiles strongly-typed handler delegates for CQRS handlers,
/// completely eliminating reflection. 
/// </summary>
internal sealed class HandlerInvoker
{
    private readonly Func<object, object, CancellationToken, ValueTask<object?>> _delegate;

    private HandlerInvoker(Func<object, object, CancellationToken, ValueTask<object?>> del)
        => _delegate = del;

    public ValueTask<object?> InvokeAsync(object handler, object request, CancellationToken ct)
        => _delegate(handler, request, ct);

    /// <summary>
    /// Factory: creates a handler invoker for the given handler/request/response.
    /// </summary>
    public static HandlerInvoker Create<THandler, TRequest, TResponse>()
        where THandler : IQueryHandler<TRequest, TResponse>
        where TRequest : IQuery<TResponse>
    {
        return new HandlerInvoker(BuildQueryInvoker<THandler, TRequest, TResponse>());
    }

    public static HandlerInvoker CreateForCommand<THandler, TCommand>()
        where THandler : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        return new HandlerInvoker(BuildCommandInvoker<THandler, TCommand>());
    }

    public static HandlerInvoker CreateForCommandWithResponse<THandler, TCommand, TResponse>()
        where THandler : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        return new HandlerInvoker(BuildCommandInvoker<THandler, TCommand, TResponse>());
    }

    // ---- Builders ----

    private static Func<object, object, CancellationToken, ValueTask<object?>>
        BuildQueryInvoker<THandler, TQuery, TResult>()
        where THandler : IQueryHandler<TQuery, TResult>
        where TQuery : IQuery<TResult>
    {
        var handlerParam = Expression.Parameter(typeof(object), "h");
        var requestParam = Expression.Parameter(typeof(object), "r");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var handlerCast = Expression.Convert(handlerParam, typeof(THandler));
        var requestCast = Expression.Convert(requestParam, typeof(TQuery));

        // handler.HandleAsync((TQuery)req, ct)
        var call = Expression.Call(handlerCast,
            typeof(THandler).GetMethod(nameof(IQueryHandler<TQuery, TResult>.Handle))!,
            requestCast, ctParam);

        // Wrap into CastValueTask
        var wrapper = Expression.Call(
            typeof(HandlerInvoker).GetMethod(nameof(CastValueTask),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(typeof(TResult)),
            call);

        return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object?>>>(
            wrapper, handlerParam, requestParam, ctParam).Compile();
    }

    private static Func<object, object, CancellationToken, ValueTask<object?>>
        BuildCommandInvoker<THandler, TCommand>()
        where THandler : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        var handlerParam = Expression.Parameter(typeof(object), "h");
        var requestParam = Expression.Parameter(typeof(object), "r");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var handlerCast = Expression.Convert(handlerParam, typeof(THandler));
        var requestCast = Expression.Convert(requestParam, typeof(TCommand));

        var call = Expression.Call(handlerCast,
            typeof(THandler).GetMethod(nameof(ICommandHandler<TCommand>.HandleAsync))!,
            requestCast, ctParam);

        // no result, just return default
        var body = Expression.Block(
            call,
            Expression.Constant(new ValueTask<object?>((object?)null))
        );

        return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object?>>>(
            body, handlerParam, requestParam, ctParam).Compile();
    }

    private static Func<object, object, CancellationToken, ValueTask<object?>>
        BuildCommandInvoker<THandler, TCommand, TResult>()
        where THandler : ICommandHandler<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        var handlerParam = Expression.Parameter(typeof(object), "h");
        var requestParam = Expression.Parameter(typeof(object), "r");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var handlerCast = Expression.Convert(handlerParam, typeof(THandler));
        var requestCast = Expression.Convert(requestParam, typeof(TCommand));

        var call = Expression.Call(handlerCast,
            typeof(THandler).GetMethod(nameof(ICommandHandler<TCommand, TResult>.HandleAsync))!,
            requestCast, ctParam);

        // Wrap into CastValueTask
        var wrapper = Expression.Call(
            typeof(HandlerInvoker).GetMethod(nameof(CastValueTask),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(typeof(TResult)),
            call);

        return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object?>>>(
            wrapper, handlerParam, requestParam, ctParam).Compile();
    }

    // ---- Helper ----
    private static async ValueTask<object?> CastValueTask<TResult>(ValueTask<TResult> task)
    {
        var result = await task.ConfigureAwait(false);
        return result is null ? null : (object)result;
    }
}
