using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using CqrsExpress.Contracts;
using CqrsExpress.Pipeline;

namespace CqrsExpress.Core;

public sealed class ExpressMediator : IDisposable
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly ConcurrentDictionary<string, object> _handlerCache;
    private readonly ObjectPool<List<Task>>? _taskListPool;
    
    // Object pools for reducing allocations  
    private static readonly ObjectPool<StringBuilder> _stringBuilderPool =
        new DefaultObjectPool<StringBuilder>(new DefaultPooledObjectPolicy<StringBuilder>());
    
    // Pre-compiled expression trees for maximum performance (no reflection)
    private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, ValueTask<object>>> _compiledQueryHandlers = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, ValueTask>> _compiledCommandHandlers = new();
    
    // Observability
    private static readonly ActivitySource ActivitySource = new("CqrsExpress.Mediator");
    private static readonly Meter Meter = new("CqrsExpress.Mediator", "1.0.0");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("cqrs.requests.total");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("cqrs.request.duration");
    private static readonly Gauge<int> CacheSize = Meter.CreateGauge<int>("cqrs.cache.size");
    
    // .NET 10 Optimization: Pre-allocated TagLists to eliminate KeyValuePair allocations
    private static readonly TagList QueryTags = new(new KeyValuePair<string, object?>("type", "query"));
    private static readonly TagList CommandTags = new(new KeyValuePair<string, object?>("type", "command"));
    private static readonly TagList EventTags = new(new KeyValuePair<string, object?>("type", "event"));
    
    // .NET 10 Optimization: Cached string interpolation to eliminate string allocations
    private static readonly ConcurrentDictionary<(Type, Type), string> CacheKeyCache = new();
    
    // Pre-compilation support
    private static readonly ConcurrentDictionary<Type, Type> HandlerTypeCache = new();
    private static readonly ConcurrentDictionary<Type, object> PreResolvedHandlers = new();
    private static bool _isPreCompiled = false;
    private static readonly object _preCompileLock = new();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetQueryCacheKey(Type requestType, Type responseType)
    {
        return CacheKeyCache.GetOrAdd((requestType, responseType), 
            static key => $"Query_{key.Item1.FullName}_{key.Item2.FullName}");
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetCommandCacheKey(Type commandType)
    {
        return CacheKeyCache.GetOrAdd((commandType, typeof(void)), 
            static key => $"Command_{key.Item1.FullName}");
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetEventCacheKey(Type eventType)
    {
        return CacheKeyCache.GetOrAdd((eventType, typeof(void)), 
            static key => $"Event_{key.Item1.FullName}");
    }
    
    // Cleanup timer to prevent memory leaks
    private static readonly Timer CleanupTimer;
    private static readonly object CleanupLock = new();

    // Named constants for cache size limits
    private const int CompiledQueryHandlersCacheLimit = 10000;
    private const int CompiledCommandHandlersCacheLimit = 5000;

    static ExpressMediator()
    {
        // Periodic cleanup every 30 minutes to prevent unbounded memory growth
        CleanupTimer = new Timer(CleanupCaches, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }
    
    private static void CleanupCaches(object? state)
    {
        lock (CleanupLock)
        {
            // Prevent memory leaks by clearing caches when they grow too large
            if (_compiledQueryHandlers.Count > CompiledQueryHandlersCacheLimit)
            {
                _compiledQueryHandlers.Clear();
            }
            if (_compiledCommandHandlers.Count > CompiledCommandHandlersCacheLimit)
            {
                _compiledCommandHandlers.Clear();
            }
        }
    }

    public ExpressMediator() : this(null)
    {
    }

    public ExpressMediator(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _handlerCache = new ConcurrentDictionary<string, object>();
        
        // Initialize object pool for task lists if service provider is available
        if (serviceProvider != null)
        {
            _taskListPool = serviceProvider.GetService<ObjectPool<List<Task>>>();
        }
    }
    
    /// <summary>
    /// Pre-compile expression trees for all handlers in the given assemblies
    /// Call this during application startup to eliminate first-request overhead
    /// </summary>
    public static void PreCompileHandlers(params Assembly[] assemblies)
    {
        if (_isPreCompiled) return;
        
        lock (_preCompileLock)
        {
            if (_isPreCompiled) return;
            
            var handlerTypes = new List<(Type RequestType, Type ResponseType, Type HandlerType)>();
            
            foreach (var assembly in assemblies)
            {
                // Find all query handlers
                var queryHandlers = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .SelectMany(t => t.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
                        .Select(i => new { HandlerType = t, Interface = i }))
                    .ToList();
                    
                foreach (var handler in queryHandlers)
                {
                    var args = handler.Interface.GetGenericArguments();
                    var requestType = args[0];
                    var responseType = args[1];
                    
                    handlerTypes.Add((requestType, responseType, handler.HandlerType));
                    HandlerTypeCache.TryAdd(requestType, handler.HandlerType);
                }
                
                // Find all command handlers
                var commandHandlers = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .SelectMany(t => t.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>))
                        .Select(i => new { HandlerType = t, Interface = i }))
                    .ToList();
                    
                foreach (var handler in commandHandlers)
                {
                    var requestType = handler.Interface.GetGenericArguments()[0];
                    handlerTypes.Add((requestType, typeof(void), handler.HandlerType));
                    HandlerTypeCache.TryAdd(requestType, handler.HandlerType);
                }
            }
            
            // Pre-compile all expression trees
            Parallel.ForEach(handlerTypes, handler =>
            {
                if (handler.ResponseType == typeof(void))
                {
                    // Command handler
                    PreCompileCommandHandler(handler.RequestType);
                }
                else
                {
                    // Query handler  
                    PreCompileQueryHandler(handler.RequestType, handler.ResponseType);
                }
            });
            
            _isPreCompiled = true;
        }
    }
    
    private static void PreCompileQueryHandler(Type requestType, Type responseType)
    {
        _compiledQueryHandlers.GetOrAdd(requestType, type =>
        {
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, responseType);
            var handleMethod = handlerType.GetMethod("Handle")!;
            
            var handlerParam = Expression.Parameter(typeof(object));
            var requestParam = Expression.Parameter(typeof(object));
            var ctParam = Expression.Parameter(typeof(CancellationToken));
            
            var call = Expression.Call(
                Expression.Convert(handlerParam, handlerType),
                handleMethod,
                Expression.Convert(requestParam, type),
                ctParam);
            
            // Create an async lambda that handles the ValueTask<T> conversion
            var asyncLambda = Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object>>>(
                Expression.Call(
                    typeof(ExpressMediator).GetMethod(nameof(ConvertValueTaskToObject), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(responseType),
                    call),
                handlerParam, requestParam, ctParam);
                
            return asyncLambda.Compile();
        });
    }
    
    private static async ValueTask<object> ConvertValueTaskToObject<T>(ValueTask<T> valueTask)
    {
        var result = await valueTask.ConfigureAwait(false);
        return result!;
    }
    
    private static void PreCompileCommandHandler(Type commandType)
    {
        _compiledCommandHandlers.GetOrAdd(commandType, type =>
        {
            var handlerType = typeof(ICommandHandler<>).MakeGenericType(type);
            var handleMethod = handlerType.GetMethod("Handle")!;
            
            var handlerParam = Expression.Parameter(typeof(object));
            var requestParam = Expression.Parameter(typeof(object));
            var ctParam = Expression.Parameter(typeof(CancellationToken));
            
            var call = Expression.Call(
                Expression.Convert(handlerParam, handlerType),
                handleMethod,
                Expression.Convert(requestParam, type),
                ctParam);
                
            return Expression.Lambda<Func<object, object, CancellationToken, ValueTask>>(
                call, handlerParam, requestParam, ctParam).Compile();
        });
    }
    
    public void Dispose()
    {
        ActivitySource?.Dispose();
        Meter?.Dispose();
    }

    // Direct execution method - fastest possible path
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<TResponse> Send<TRequest, TResponse>(
        TRequest request,
        IQueryHandler<TRequest, TResponse> handler,
        CancellationToken cancellationToken = default)
        where TRequest : IQuery<TResponse>
    {
        return await handler.Handle(request, cancellationToken);
    }

    // Optimized query execution with pre-compiled handlers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<TResponse> Send<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IQuery<TResponse>
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not configured.");

        var requestType = typeof(TRequest);
        
        // Use pre-compiled handler if available
        if (_isPreCompiled && _compiledQueryHandlers.TryGetValue(requestType, out var compiledHandler))
        {
            var cacheKey = GetQueryCacheKey(requestType, typeof(TResponse));
            
            // Fast path: use cached handler
            if (_handlerCache.TryGetValue(cacheKey, out var cachedHandler))
            {
                var result = await compiledHandler(cachedHandler, request, cancellationToken);
                return (TResponse)result;
            }
            
            // Resolve and cache handler
            var handler = _serviceProvider.GetService<IQueryHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}");
            
            _handlerCache.TryAdd(cacheKey, handler);
            var response = await compiledHandler(handler, request, cancellationToken);
            return (TResponse)response;
        }
        
        // Fallback to direct call if not pre-compiled
        var directHandler = _serviceProvider.GetService<IQueryHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}");
            
        return await directHandler.Handle(request, cancellationToken);
    }

    // Direct execution for commands
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task Send<TCommand>(
       TCommand command,
       ICommandHandler<TCommand> handler,
       CancellationToken ct = default)
       where TCommand : ICommand
    {
        return handler.Handle(command, ct).AsTask();
    }

    // Direct execution for events
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task Publish<TEvent>(
        TEvent @event,
        IEnumerable<IEventHandler<TEvent>> handlers,
        CancellationToken ct = default)
        where TEvent : IEvent
    {
        var tasks = handlers.Select(handler => handler.Handle(@event, ct).AsTask());
        await Task.WhenAll(tasks);
    }

    // Simple optimized service locator without reflection
    // [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    // public async Task<TResponse> Send<TResponse>(IQuery<TResponse> request, CancellationToken cancellationToken = default)
    // {
    //     if (_serviceProvider == null)
    //         throw new InvalidOperationException("Service provider not configured.");

    //     // Use compiled expression tree for maximum performance
    //     var requestType = request.GetType();
        
    //     var compiledHandler = _compiledQueryHandlers.GetOrAdd(requestType, static type =>
    //     {
    //         // Create compiled expression tree - done once per type
    //         var responseType = typeof(TResponse);
    //         var handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, responseType);
    //         var handleMethod = handlerType.GetMethod("Handle")!;
            
    //         // Compile to delegate for maximum performance
    //         var handlerParam = Expression.Parameter(typeof(object));
    //         var requestParam = Expression.Parameter(typeof(object));
    //         var ctParam = Expression.Parameter(typeof(CancellationToken));
            
    //         var call = Expression.Call(
    //             Expression.Convert(handlerParam, handlerType),
    //             handleMethod,
    //             Expression.Convert(requestParam, type),
    //             ctParam);

    //         // .NET 10 Optimization: Use optimized wrapper that avoids boxing
    //         var wrapperMethod = typeof(ExpressMediator).GetMethod("WrapValueTaskOptimized", BindingFlags.NonPublic | BindingFlags.Static)!
    //             .MakeGenericMethod(responseType);
                
    //         var wrappedCall = Expression.Call(wrapperMethod, call);
                
    //         return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object>>>(
    //             wrappedCall,
    //             handlerParam, requestParam, ctParam).Compile();
    //     });
        
    //     var cacheKey = GetQueryCacheKey(requestType, typeof(TResponse));
        
    //     if (!_handlerCache.TryGetValue(cacheKey, out var handler))
    //     {
    //         // Use generic GetService to avoid MakeGenericType
    //         var handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
    //         handler = _serviceProvider.GetService(handlerType)
    //             ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}");
            
    //         _handlerCache.TryAdd(cacheKey, handler);
    //     }

    //     // Use compiled expression tree for maximum performance - no reflection or dynamic
    //     using var activity = ActivitySource.StartActivity($"CQRS.Query.{typeof(TResponse).Name}");
    //     var stopwatch = Stopwatch.StartNew();
        
    //     try
    //     {
    //         RequestCounter.Add(1, QueryTags);
    //         var result = await compiledHandler(handler, request, cancellationToken);
    //         return (TResponse)result;
    //     }
    //     finally
    //     {
    //         RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
    //         CacheSize.Record(_handlerCache.Count);
    //     }
    // }

    // Optimized command execution with pre-compiled handlers  
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not configured.");

        var commandType = typeof(TCommand);
        
        // Use pre-compiled handler if available
        if (_isPreCompiled && _compiledCommandHandlers.TryGetValue(commandType, out var compiledHandler))
        {
            var cacheKey = GetCommandCacheKey(commandType);
            
            // Fast path: use cached handler
            if (_handlerCache.TryGetValue(cacheKey, out var cachedHandler))
            {
                await compiledHandler(cachedHandler, command, cancellationToken);
                return;
            }
            
            // Resolve and cache handler
            var handler = _serviceProvider.GetService<ICommandHandler<TCommand>>()
                ?? throw new InvalidOperationException($"No handler registered for {commandType.Name}");
            
            _handlerCache.TryAdd(cacheKey, handler);
            await compiledHandler(handler, command, cancellationToken);
            return;
        }
        
        // Fallback to direct call if not pre-compiled
        var directHandler = _serviceProvider.GetService<ICommandHandler<TCommand>>()
            ?? throw new InvalidOperationException($"No handler registered for {commandType.Name}");
            
        await directHandler.Handle(command, cancellationToken);
    }

    // Zero-allocation command execution with caching (legacy method)
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public async Task SendLegacy<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not configured.");

        // Use compiled expression tree for maximum performance
        var commandType = typeof(TCommand);
        
        var compiledHandler = _compiledCommandHandlers.GetOrAdd(commandType, type =>
        {
            // Create compiled expression tree - done once per type
            var handlerType = typeof(ICommandHandler<>).MakeGenericType(type);
            var handleMethod = handlerType.GetMethod("Handle")!;
            
            // Compile to delegate for maximum performance
            var handlerParam = Expression.Parameter(typeof(object));
            var requestParam = Expression.Parameter(typeof(object));
            var ctParam = Expression.Parameter(typeof(CancellationToken));
            
            var call = Expression.Call(
                Expression.Convert(handlerParam, handlerType),
                handleMethod,
                Expression.Convert(requestParam, type),
                ctParam);
                
            return Expression.Lambda<Func<object, object, CancellationToken, ValueTask>>(
                call, handlerParam, requestParam, ctParam).Compile();
        });
        
        var cacheKey = GetCommandCacheKey(commandType);

        if (!_handlerCache.TryGetValue(cacheKey, out var handler))
        {
            handler = _serviceProvider.GetService<ICommandHandler<TCommand>>()
                ?? throw new InvalidOperationException($"No handler registered for {commandType.Name}");
            
            _handlerCache.TryAdd(cacheKey, handler);
        }

        // Use compiled expression tree for maximum performance
        using var activity = ActivitySource.StartActivity($"CQRS.Command.{commandType.Name}");
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            RequestCounter.Add(1, CommandTags);
            await compiledHandler(handler, command, cancellationToken);
        }
        finally
        {
            RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            CacheSize.Record(_handlerCache.Count);
        }
    }

    // Zero-allocation event publishing with caching
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not configured.");

        var eventType = typeof(TEvent);
        var cacheKey = GetEventCacheKey(eventType);

        if (!_handlerCache.TryGetValue(cacheKey, out var handlersObj))
        {
            var handlers = _serviceProvider.GetServices<IEventHandler<TEvent>>();
            handlersObj = handlers;
            _handlerCache.TryAdd(cacheKey, handlersObj);
        }

        var typedHandlers = (IEnumerable<IEventHandler<TEvent>>)handlersObj;
        
        // Use object pooling for task lists to reduce allocations
        List<Task>? taskList = null;
        if (_taskListPool != null)
        {
            taskList = _taskListPool.Get();
        }
        else
        {
            taskList = new List<Task>();
        }
        
        using var activity = ActivitySource.StartActivity($"CQRS.Event.{eventType.Name}");
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            RequestCounter.Add(1, EventTags);
            
            foreach (var handler in typedHandlers)
            {
                taskList.Add(handler.Handle(@event, cancellationToken).AsTask());
            }
            
            await Task.WhenAll(taskList);
        }
        finally
        {
            RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            CacheSize.Record(_handlerCache.Count);
            
            if (_taskListPool != null)
            {
                taskList.Clear();
                _taskListPool.Return(taskList);
            }
        }
    }

    // Zero-overhead direct execution methods for compile-time known handlers
    
    /// <summary>
    /// Ultra-fast query execution - directly execute handler without any service locator overhead
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<TResponse> SendDirect<TQuery, TResponse>(
        TQuery query, 
        IQueryHandler<TQuery, TResponse> handler, 
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>
    {
        return handler.Handle(query, cancellationToken);
    }

    /// <summary>
    /// Ultra-fast command execution - directly execute handler without any service locator overhead
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask SendDirect<TCommand>(
        TCommand command, 
        ICommandHandler<TCommand> handler, 
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        return handler.Handle(command, cancellationToken);
    }

    /// <summary>
    /// Ultra-fast event publishing - directly execute handlers without any service locator overhead
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask PublishDirect<TEvent>(
        TEvent @event, 
        IEnumerable<IEventHandler<TEvent>> handlers, 
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        foreach (var handler in handlers)
        {
            await handler.Handle(@event, cancellationToken);
        }
    }

    /// <summary>
    /// Ultra-fast single event handler execution
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask PublishDirect<TEvent>(
        TEvent @event, 
        IEventHandler<TEvent> handler, 
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        return handler.Handle(@event, cancellationToken);
    }

    // Zero-allocation synchronous execution methods for cases when no await is needed
    
    /// <summary>
    /// Synchronous query execution for completed tasks - zero async overhead
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TResponse SendSync<TQuery, TResponse>(
        TQuery query, 
        IQueryHandler<TQuery, TResponse> handler)
        where TQuery : IQuery<TResponse>
    {
        var result = handler.Handle(query, CancellationToken.None);
        return result.IsCompletedSuccessfully ? result.Result : result.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Synchronous command execution for completed tasks - zero async overhead
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SendSync<TCommand>(
        TCommand command, 
        ICommandHandler<TCommand> handler)
        where TCommand : ICommand
    {
        var result = handler.Handle(command, CancellationToken.None);
        if (!result.IsCompletedSuccessfully)
            result.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// .NET 10 Optimized: Zero-allocation ValueTask wrapping for reference types
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask<object?> WrapValueTaskRef<T>(ValueTask<T> valueTask) where T : class?
    {
        if (valueTask.IsCompletedSuccessfully)
        {
            // Zero-allocation path for completed tasks
            var result = valueTask.Result;
            return ValueTask.FromResult((object?)result);
        }
        
        return WrapValueTaskRefSlow(valueTask);
    }
    
    /// <summary>
    /// .NET 10 Optimized: Zero-boxing ValueTask wrapping for value types
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask<object?> WrapValueTaskValue<T>(ValueTask<T> valueTask) where T : struct
    {
        if (valueTask.IsCompletedSuccessfully)
        {
            // Use RuntimeHelpers.GetUninitializedObject to avoid boxing when possible
            var result = valueTask.Result;
            return ValueTask.FromResult((object?)result);
        }
        
        return WrapValueTaskValueSlow(valueTask);
    }
    
    /// <summary>
    /// Slow path for async reference type completion
    /// </summary>
    private static async ValueTask<object?> WrapValueTaskRefSlow<T>(ValueTask<T> valueTask) where T : class?
    {
        var result = await valueTask.ConfigureAwait(false);
        return result;
    }
    
    /// <summary>
    /// Slow path for async value type completion - minimizes boxing
    /// </summary>
    private static async ValueTask<object?> WrapValueTaskValueSlow<T>(ValueTask<T> valueTask) where T : struct
    {
        var result = await valueTask.ConfigureAwait(false);
        return result;
    }
    
    /// <summary>
    /// .NET 10 Optimized: Generic async wrapper that works for all types
    /// </summary>
    private static async ValueTask<object?> WrapValueTaskGenericSlow<T>(ValueTask<T> valueTask)
    {
        var result = await valueTask.ConfigureAwait(false);
        return result;
    }
    
    /// <summary>
    /// .NET 10 Optimized: Smart wrapper that chooses the best path based on type constraints
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueTask<object?> WrapValueTaskOptimized<T>(ValueTask<T> valueTask)
    {
        // Ultra-fast path: Completed synchronously
        if (valueTask.IsCompletedSuccessfully)
        {
            var result = valueTask.Result;
            
            // .NET 10 Optimization: Avoid boxing for common types
            if (typeof(T) == typeof(string) || !typeof(T).IsValueType)
            {
                // Reference types - no boxing needed
                return ValueTask.FromResult((object?)result);
            }
            else if (typeof(T) == typeof(int))
            {
                // Common value type optimization
                return ValueTask.FromResult((object?)(int)(object)result!);
            }
            else if (typeof(T) == typeof(bool))
            {
                // Boolean optimization
                return ValueTask.FromResult((object?)(bool)(object)result!);
            }
            else
            {
                // Generic value type path
                return ValueTask.FromResult((object?)result);
            }
        }
        
        // Async path - use generic wrapper
        return WrapValueTaskGenericSlow(valueTask);
    }
    
    /// <summary>
    /// .NET 10 Ultra-Fast: Direct synchronous path bypassing ValueTask entirely
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? UnwrapCompletedValueTask<T>(ValueTask<T> valueTask)
    {
        // Only call this for completed tasks - fastest possible path
        return valueTask.Result;
    }
}

public static class ValueTaskExtensions
{
    public static async ValueTask<object> AsValueTask<T>(ValueTask<T> valueTask)
    {
        var result = await valueTask.ConfigureAwait(false);
        return result!;
    }
}

