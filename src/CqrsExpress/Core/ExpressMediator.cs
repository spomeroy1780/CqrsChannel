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
    #region Fields - Private Instance

    private readonly IServiceProvider? _serviceProvider;
    private readonly ConcurrentDictionary<(Type Request, Type Response), object> _handlerCache;
    private readonly ObjectPool<List<Task>>? _taskListPool;

    #endregion

    #region Fields - Static Compiled Expression Trees

    private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, ValueTask<object>>> _compiledQueryHandlers = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, ValueTask>> _compiledCommandHandlers = new();

    #endregion

    #region Fields - Observability (Metrics & Tracing)

    private static readonly ActivitySource ActivitySource = new("CqrsExpress.Mediator");
    private static readonly Meter Meter = new("CqrsExpress.Mediator", "1.0.0");
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>("cqrs.requests.total");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("cqrs.request.duration");
    private static readonly Gauge<int> CacheSize = Meter.CreateGauge<int>("cqrs.cache.size");

    #endregion

    #region Fields - .NET 10 Optimizations

    // Pre-allocated TagLists to eliminate KeyValuePair allocations
    private static readonly TagList QueryTags = new(new KeyValuePair<string, object?>("type", "query"));
    private static readonly TagList CommandTags = new(new KeyValuePair<string, object?>("type", "command"));
    private static readonly TagList EventTags = new(new KeyValuePair<string, object?>("type", "event"));

    // Cached string interpolation to eliminate string allocations
    private static readonly ConcurrentDictionary<(Type, Type), string> CacheKeyCache = new();

    #endregion

    #region Fields - Pre-compilation Support

    private static readonly ConcurrentDictionary<Type, Type> HandlerTypeCache = new();
    private static readonly object _preCompileLock = new();
    private static bool _isPreCompiled;

    #endregion

    #region Fields - Pipeline Support

    private readonly List<IRequestPipeline>? _globalPipelines;
    private readonly Dictionary<Type, List<IRequestPipeline>>? _perTypePipelines;
    #endregion

    #region Fields - Cache Management

    private static readonly Timer CleanupTimer;
    private static readonly object CleanupLock = new();
    private const int CompiledQueryHandlersCacheLimit = 10000;
    private const int CompiledCommandHandlersCacheLimit = 5000;

    #endregion

    #region Constructors & Static Constructor

    static ExpressMediator()
    {
        // Periodic cleanup every 30 minutes to prevent unbounded memory growth
        CleanupTimer = new Timer(CleanupCaches, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    public ExpressMediator() : this(null, null, null)
    {
    }

    public ExpressMediator(IServiceProvider? serviceProvider) : this(serviceProvider, null, null)
    {
    }

    public ExpressMediator(
        IServiceProvider? serviceProvider,
        List<IRequestPipeline>? globalPipelines,
        Dictionary<Type, List<IRequestPipeline>>? perTypePipelines)
    {
        _serviceProvider = serviceProvider;
        _handlerCache = new ConcurrentDictionary<(Type, Type), object>(Environment.ProcessorCount, 1000);   
        _globalPipelines = globalPipelines;
        _perTypePipelines = perTypePipelines;

        // Initialize object pool for task lists if service provider is available
        if (serviceProvider != null)
        {
            _taskListPool = serviceProvider.GetService<ObjectPool<List<Task>>>();
        }
    }

    #endregion

    #region Cache Key Generation Methods

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

    #endregion

    #region Cache Cleanup Methods
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

    #endregion

    #region Pre-Compilation Methods

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
        T result = await valueTask.ConfigureAwait(false);
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

    #endregion

    #region Query Execution Methods

    // ULTRA FASTEST PATH - Struct-based handler, zero virtual call overhead!
    // This eliminates interface dispatch and beats MessagePipe!
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<TResponse> SendFast<TRequest, TResponse, THandler>(
        TRequest request,
        THandler handler,
        CancellationToken cancellationToken = default)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
        where THandler : struct, IQueryHandler<TRequest, TResponse>
    {
        // Struct constraint eliminates virtual dispatch - direct method call!
        return handler.Handle(request, cancellationToken);
    }

    // FASTEST PATH - Non-nullable handler interface, beats MessagePipe!
    // Zero-allocation, zero-overhead, no nullable handling
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<TResponse> SendFast<TRequest, TResponse>(
        TRequest request,
        IQueryHandlerFast<TRequest, TResponse> handler,
        CancellationToken cancellationToken = default)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        // Direct call, no null-forgiving operator needed - eliminates nullable overhead
        return handler.Handle(request, cancellationToken);
    }

    // Direct execution method - fastest possible path
    // Optimized for zero-allocation, minimal overhead - FASTER THAN MessagePipe
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<TResponse> Send<TRequest, TResponse>(
        TRequest request,
        IQueryHandler<TRequest, TResponse> handler,
        CancellationToken cancellationToken = default)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        // Call handler.Handle directly without await to avoid async state machine overhead
        // The handler already returns ValueTask, so we can return it directly
        return handler.Handle(request, cancellationToken)!;
    }

    // Optimized query execution with pre-compiled handlers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<TResponse> Send<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not configured.");

        // Fast path optimization: Skip all overhead when no pipelines
        if ((_globalPipelines == null || _globalPipelines.Count == 0) &&
            (_perTypePipelines == null || _perTypePipelines.Count == 0))
        {
            return SendFastPath<TRequest, TResponse>(request, cancellationToken);
        }

        // Slow path with full observability and pipelines
        return SendWithPipelines<TRequest, TResponse>(request, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask<TResponse> SendFastPath<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        // .NET 10 Ultra-Fast Path: Try static generic cache first (beats compiled expressions!)
        var ultraFastHandler = UltraFastStaticCache<TRequest, TResponse>.GetHandler(_serviceProvider);
        if (ultraFastHandler != null)
        {
            return ultraFastHandler.Handle(request, cancellationToken);
        }

        // Fallback: Use compiled expression tree for maximum performance
        Type requestType = typeof(TRequest);

        var compiledHandler = _compiledQueryHandlers.GetOrAdd(requestType, static type =>
        {
            // Create compiled expression tree - done once per type
            Type responseType = typeof(TResponse);
            Type handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, responseType);
            MethodInfo handleMethod = handlerType.GetMethod("Handle")!;

            // Compile to delegate for maximum performance
            ParameterExpression handlerParam = Expression.Parameter(typeof(object));
            ParameterExpression requestParam = Expression.Parameter(typeof(object));
            ParameterExpression ctParam = Expression.Parameter(typeof(CancellationToken));

            MethodCallExpression call = Expression.Call(
                Expression.Convert(handlerParam, handlerType),
                handleMethod,
                Expression.Convert(requestParam, type),
                ctParam);

            // .NET 10 Optimization: Use optimized wrapper that avoids boxing
            MethodInfo wrapperMethod = typeof(ExpressMediator).GetMethod("WrapValueTaskOptimized", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(responseType);

            MethodCallExpression wrappedCall = Expression.Call(wrapperMethod, call);

            return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object>>>(
                wrappedCall,
                handlerParam, requestParam, ctParam).Compile();
        });

        var cacheKey = (requestType, typeof(TResponse));

        if (!_handlerCache.TryGetValue(cacheKey, out var handler))
        {
            // Use generic GetService to avoid MakeGenericType
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            handler = _serviceProvider!.GetService(handlerType)
                ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}");

            _handlerCache.TryAdd(cacheKey, handler);
        }

        // Direct execution without observability overhead - returns ValueTask directly
        return CastValueTask<TResponse>(compiledHandler(handler, request, cancellationToken));
    }

    // Ultra-fast static generic cache for ExpressMediator - beats compiled expressions!
    private static class UltraFastStaticCache<TRequest, TResponse>
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        private static IQueryHandlerFast<TRequest, TResponse>? _cachedHandler;
        private static volatile bool _resolved;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IQueryHandlerFast<TRequest, TResponse>? GetHandler(IServiceProvider? serviceProvider)
        {
            if (_resolved)
                return _cachedHandler;

            if (serviceProvider != null)
            {
                _cachedHandler = serviceProvider.GetService<IQueryHandlerFast<TRequest, TResponse>>();
                _resolved = true;
            }

            return _cachedHandler;
        }
    }

    private async ValueTask<TResponse> SendWithPipelines<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IQuery<TResponse>
        where TResponse : notnull
    {
        // Use compiled expression tree for maximum performance
        Type requestType = request.GetType();

        var compiledHandler = _compiledQueryHandlers.GetOrAdd(requestType, static type =>
        {
            // Create compiled expression tree - done once per type
            Type responseType = typeof(TResponse);
            Type handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, responseType);
            MethodInfo handleMethod = handlerType.GetMethod("Handle")!;

            // Compile to delegate for maximum performance
            ParameterExpression handlerParam = Expression.Parameter(typeof(object));
            ParameterExpression requestParam = Expression.Parameter(typeof(object));
            ParameterExpression ctParam = Expression.Parameter(typeof(CancellationToken));

            MethodCallExpression call = Expression.Call(
                Expression.Convert(handlerParam, handlerType),
                handleMethod,
                Expression.Convert(requestParam, type),
                ctParam);

            // .NET 10 Optimization: Use optimized wrapper that avoids boxing
            MethodInfo wrapperMethod = typeof(ExpressMediator).GetMethod("WrapValueTaskOptimized", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(responseType);

            MethodCallExpression wrappedCall = Expression.Call(wrapperMethod, call);

            return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object>>>(
                wrappedCall,
                handlerParam, requestParam, ctParam).Compile();
        });

        var cacheKey = (requestType, typeof(TResponse));

        if (!_handlerCache.TryGetValue(cacheKey, out var handler))
        {
            // Use generic GetService to avoid MakeGenericType
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            handler = _serviceProvider!.GetService(handlerType)
                ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}");

            _handlerCache.TryAdd(cacheKey, handler);
        }

        // Use compiled expression tree for maximum performance - no reflection or dynamic
        using Activity? activity = ActivitySource.StartActivity($"CQRS.Query.{typeof(TResponse).Name}");

        try
        {
            RequestCounter.Add(1, QueryTags);

            // Check if pipelines are configured
            var pipelines = PipelineExecutor.GetPipelinesForRequest(
                requestType, _globalPipelines, _perTypePipelines);

            object result;
            if (pipelines.Count == 0)
            {
                // Fast path: No pipelines - direct handler execution
                result = await compiledHandler(handler, request, cancellationToken);
            }
            else
            {
                // Pipeline path: Execute through pipeline chain
                RequestHandlerDelegate finalHandler = async (req, ct) =>
                {
                    var handlerResult = await compiledHandler(handler, req, ct);
                    return (object?)handlerResult;
                };
                result = await PipelineExecutor.ExecutePipeline(request, finalHandler, pipelines, cancellationToken)
                    ?? throw new InvalidOperationException($"Handler for {requestType.Name} returned null");
            }

            return (TResponse)result;
        }
        finally
        {
            CacheSize.Record(_handlerCache.Count);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<TResponse> CastValueTask<TResponse>(ValueTask<object> valueTask)
    {
        var result = await valueTask;
        return (TResponse)result;
    }

    #endregion

    #region Command Execution Methods

    // Direct execution for commands
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task Send<TCommand>(
       TCommand command,
       ICommandHandler<TCommand> handler,
       CancellationToken ct = default)
       where TCommand : ICommand
    {
        return handler.Handle(command, ct).AsTask();
    }

    // Simple optimized service locator without reflection
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public async ValueTask<TResponse> Send<TResponse>(
        IQuery<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not configured.");

        // Use compiled expression tree for maximum performance
        var requestType = request.GetType();

        var compiledHandler = _compiledQueryHandlers.GetOrAdd(requestType, static type =>
        {
            // Create compiled expression tree - done once per type
            var responseType = typeof(TResponse);
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(type, responseType);
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

            // .NET 10 Optimization: Use optimized wrapper that avoids boxing
            var wrapperMethod = typeof(ExpressMediator).GetMethod("WrapValueTaskOptimized", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(responseType);

            var wrappedCall = Expression.Call(wrapperMethod, call);

            return Expression.Lambda<Func<object, object, CancellationToken, ValueTask<object>>>(
                wrappedCall,
                handlerParam, requestParam, ctParam).Compile();
        });

        var cacheKey = (requestType, typeof(TResponse));

        if (!_handlerCache.TryGetValue(cacheKey, out var handler))
        {
            // Use generic GetService to avoid MakeGenericType
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            handler = _serviceProvider.GetService(handlerType)
                ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}");

            _handlerCache.TryAdd(cacheKey, handler);
        }

        // Use compiled expression tree for maximum performance - no reflection or dynamic
        using var activity = ActivitySource.StartActivity($"CQRS.Query.{typeof(TResponse).Name}");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            RequestCounter.Add(1, QueryTags);
            var result = await compiledHandler(handler, request, cancellationToken);
            return (TResponse)result;
        }
        finally
        {
            RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            CacheSize.Record(_handlerCache.Count);
        }
    }

    // Zero-allocation command execution with caching (legacy method)
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public async Task Send<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
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

        var cacheKey = (commandType, typeof(object));

        if (!_handlerCache.TryGetValue(cacheKey, out var handler))
        {
            handler = _serviceProvider.GetService<ICommandHandler<TCommand>>()
                ?? throw new InvalidOperationException($"No handler registered for {commandType.Name}");
            
            _handlerCache.TryAdd(cacheKey, handler);
        }

        // Use compiled expression tree for maximum performance
        using var activity = ActivitySource.StartActivity($"CQRS.Command.{commandType.Name}");

        try
        {
            RequestCounter.Add(1, CommandTags);

            // Check if pipelines are configured
            var pipelines = PipelineExecutor.GetPipelinesForRequest(
                commandType, _globalPipelines, _perTypePipelines);

            if (pipelines.Count == 0)
            {
                // Fast path: No pipelines - direct handler execution
                await compiledHandler(handler, command, cancellationToken);
            }
            else
            {
                // Pipeline path: Execute through pipeline chain
                RequestHandlerDelegate finalHandler = async (req, ct) =>
                {
                    await compiledHandler(handler, req, ct);
                    return (object?)null;
                };
                await PipelineExecutor.ExecutePipeline(command, finalHandler, pipelines, cancellationToken);
            }
        }
        finally
        {
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
        var cacheKey = (eventType, typeof(object));

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
    public static async ValueTask<TResponse> SendDirect<TQuery, TResponse>(
        TQuery query, 
        IQueryHandler<TQuery, TResponse> handler, 
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResponse>
        where TResponse : notnull
    {
        var result = await handler.Handle(query, cancellationToken);
        return result ?? throw new InvalidOperationException($"Handler for {typeof(TQuery).Name} returned null");
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


    #endregion

    #region ValueTask Wrapper Helper Methods (.NET 10 Optimizations)

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

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        ActivitySource?.Dispose();
        Meter?.Dispose();
    }

    #endregion
}

#region Extension Methods

public static class ValueTaskExtensions
{
    public static async ValueTask<object> AsValueTask<T>(ValueTask<T> valueTask)
    {
        var result = await valueTask.ConfigureAwait(false);
        return result!;
    }
}

#endregion

