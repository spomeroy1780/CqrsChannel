using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CqrsExpress.Contracts;
using CqrsExpress.Core;
using CqrsExpress.Core.Aot;
using CqrsExpress.DependencyInjection;
using CqrsExpress.Pipelines;
using CqrsBenchmarks.MessagePipeImpl;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsBenchmarks;

/// <summary>
/// Benchmarks comparing AOT-compatible AotExpressMediator vs standard ExpressMediator vs MediatR vs MessagePipe
/// Tests both with and without pipelines to measure overhead
/// </summary>
[MemoryDiagnoser]
public class AotBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private AotExpressMediator _aotMediator = null!;
    private ExpressMediator _expressMediator = null!;
    private IMediator _mediatr = null!;
    private MessagePipeHandler _messagePipeHandler = null!;
    
    private AotQuery _aotQuery = null!;
    private ExpressQuery _expressQuery = null!;
    private MediatRQuery _mediatrQuery = null!;
    private MessagePipeQuery _messagePipeQuery = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Register AotExpressMediator
        services.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<AotQuery, UserDto>();
        });
        services.AddSingleton<IQueryHandler<AotQuery, UserDto>, AotQueryHandler>();
        
        // Register the fast handler struct for AOT optimizations
        services.AddSingleton<IQueryHandlerFast<AotQuery, UserDto>>(new AotQueryHandlerStruct());

        // Register standard ExpressMediator with precompiled handlers
        services.AddSingleton<IQueryHandler<ExpressQuery, UserDto>, ExpressQueryHandler>();
        
        // Add ultra-fast handler for ExpressMediator optimization  
        services.AddSingleton<IQueryHandlerFast<ExpressQuery, UserDto>, ExpressQueryHandlerFast>();
        
        ExpressMediator.PreCompileHandlers(typeof(AotBenchmarks).Assembly);
        services.AddSingleton<ExpressMediator>();

        // Register MediatR
        services.AddMediatR(typeof(AotBenchmarks).Assembly);

        // Setup MessagePipe
        _messagePipeHandler = (MessagePipeHandler)MessagePipeSetup.BuildHandler();

        _serviceProvider = services.BuildServiceProvider();
        _aotMediator = _serviceProvider.GetRequiredService<AotExpressMediator>();
        _expressMediator = _serviceProvider.GetRequiredService<ExpressMediator>();
        _mediatr = _serviceProvider.GetRequiredService<IMediator>();

        _aotQuery = new AotQuery(1);
        _expressQuery = new ExpressQuery(1);
        _mediatrQuery = new MediatRQuery(1);
        _messagePipeQuery = new MessagePipeQuery(1);
    }

    [Benchmark(Baseline = true)]
    public Task<UserDto> MediatR_Query()
    {
        return _mediatr.Send(_mediatrQuery);
    }

    [Benchmark]
    public ValueTask<UserDto> MessagePipe_Query()
    {
        return _messagePipeHandler.InvokeAsync(_messagePipeQuery);
    }

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_Query()
    {
        return _expressMediator.Send<ExpressQuery, UserDto>(_expressQuery);
    }

    [Benchmark]
    public ValueTask<UserDto> AotExpressMediator_Query()
    {
        return _aotMediator.Send<AotQuery, UserDto>(_aotQuery);
    }
}

/// <summary>
/// Benchmarks comparing performance with pipelines enabled
/// Shows overhead of pipeline execution across implementations
/// </summary>
[MemoryDiagnoser]
public class AotPipelineBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private AotExpressMediator _aotMediatorWithPipeline = null!;
    private ExpressMediator _expressMediatorWithPipeline = null!;
    private IMediator _mediatrWithPipeline = null!;
    
    private AotPipelineQuery _aotQuery = null!;
    private ExpressAotPipelineQuery _expressQuery = null!;
    private MediatRPipelineQuery _mediatrQuery = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Register AotExpressMediator with pipelines
        services.AddAotExpressMediatorWithPipelines(
            registry =>
            {
                registry.RegisterQuery<AotPipelineQuery, UserDto>();
            },
            options =>
            {
                options.AddGlobalPipeline<TimingPipeline>();
            });
        services.AddSingleton<IQueryHandler<AotPipelineQuery, UserDto>, AotPipelineQueryHandler>();
        services.AddSingleton<TimingPipeline>();

        // Register standard ExpressMediator with pipelines and precompiled handlers
        services.AddSingleton<IQueryHandler<ExpressAotPipelineQuery, UserDto>, ExpressAotPipelineQueryHandler>();
        ExpressMediator.PreCompileHandlers(typeof(AotPipelineBenchmarks).Assembly);
        services.AddExpressMediatorWithPipelines(options =>
        {
            options.AddGlobalPipeline<TimingPipeline>();
        });

        // Register MediatR with pipeline behavior
        services.AddMediatR(typeof(AotPipelineBenchmarks).Assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TimingPipelineBehavior<,>));

        _serviceProvider = services.BuildServiceProvider();
        _aotMediatorWithPipeline = _serviceProvider.GetRequiredService<AotExpressMediator>();
        _expressMediatorWithPipeline = _serviceProvider.GetRequiredService<ExpressMediator>();
        _mediatrWithPipeline = _serviceProvider.GetRequiredService<IMediator>();

        _aotQuery = new AotPipelineQuery(1);
        _expressQuery = new ExpressAotPipelineQuery(1);
        _mediatrQuery = new MediatRPipelineQuery(1);
    }

    [Benchmark(Baseline = true)]
    public ValueTask<UserDto> MediatR_WithPipeline()
    {
        return new ValueTask<UserDto>(_mediatrWithPipeline.Send(_mediatrQuery));
    }

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_WithPipeline()
    {
        return _expressMediatorWithPipeline.Send<ExpressAotPipelineQuery, UserDto>(_expressQuery);
    }

    [Benchmark]
    public ValueTask<UserDto> AotExpressMediator_WithPipeline()
    {
        return _aotMediatorWithPipeline.Send<AotPipelineQuery, UserDto>(_aotQuery);
    }
}

/// <summary>
/// Direct comparison between AotExpressMediator and standard ExpressMediator
/// </summary>
[MemoryDiagnoser]
public class AotVsStandardBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private AotExpressMediator _aotMediator = null!;
    private ExpressMediator _standardMediator = null!;
    private AotDirectQuery _aotQuery = null!;
    private StandardDirectQuery _standardQuery = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // AOT variant
        services.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<AotDirectQuery, UserDto>();
        });
        services.AddSingleton<IQueryHandler<AotDirectQuery, UserDto>, AotDirectQueryHandler>();

        // Standard variant with precompiled handlers
        services.AddSingleton<IQueryHandler<StandardDirectQuery, UserDto>, StandardDirectQueryHandler>();
        ExpressMediator.PreCompileHandlers(typeof(AotVsStandardBenchmarks).Assembly);
        services.AddSingleton<ExpressMediator>();

        _serviceProvider = services.BuildServiceProvider();
        _aotMediator = _serviceProvider.GetRequiredService<AotExpressMediator>();
        _standardMediator = _serviceProvider.GetRequiredService<ExpressMediator>();

        _aotQuery = new AotDirectQuery(1);
        _standardQuery = new StandardDirectQuery(1);
    }

    [Benchmark(Baseline = true)]
    public async Task<UserDto> StandardExpressMediator()
    {
        return await _standardMediator.Send<StandardDirectQuery, UserDto>(_standardQuery);
    }

    [Benchmark]
    public async Task<UserDto> AotExpressMediator()
    {
        return await _aotMediator.Send<AotDirectQuery, UserDto>(_aotQuery);
    }
}

// ============================================================================
// Test Queries and Handlers - AOT Variants
// ============================================================================

public record AotQuery(int UserId) : IQuery<UserDto>;

public class AotQueryHandler : IQueryHandler<AotQuery, UserDto>
{
    public ValueTask<UserDto> Handle(AotQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

public record AotPipelineQuery(int UserId) : IQuery<UserDto>;

public class AotPipelineQueryHandler : IQueryHandler<AotPipelineQuery, UserDto>
{
    public ValueTask<UserDto> Handle(AotPipelineQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

public record AotDirectQuery(int UserId) : IQuery<UserDto>;

public class AotDirectQueryHandler : IQueryHandler<AotDirectQuery, UserDto>
{
    public ValueTask<UserDto> Handle(AotDirectQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

// ============================================================================
// Test Queries and Handlers - Standard ExpressMediator Variants
// ============================================================================

public record ExpressQuery(int UserId) : IQuery<UserDto>;

public class ExpressQueryHandler : IQueryHandler<ExpressQuery, UserDto>
{
    public ValueTask<UserDto> Handle(ExpressQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

public class ExpressQueryHandlerFast : IQueryHandlerFast<ExpressQuery, UserDto>
{
    public ValueTask<UserDto> Handle(ExpressQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

public record ExpressAotPipelineQuery(int UserId) : IQuery<UserDto>;

public class ExpressAotPipelineQueryHandler : IQueryHandler<ExpressAotPipelineQuery, UserDto>
{
    public ValueTask<UserDto> Handle(ExpressAotPipelineQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

public record StandardDirectQuery(int UserId) : IQuery<UserDto>;

public class StandardDirectQueryHandler : IQueryHandler<StandardDirectQuery, UserDto>
{
    public ValueTask<UserDto> Handle(StandardDirectQuery query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
    }
}

// ============================================================================
// Test Queries and Handlers - MediatR Variants
// ============================================================================

public record MediatRQuery(int UserId) : IRequest<UserDto>;

public class MediatRQueryHandler : IRequestHandler<MediatRQuery, UserDto>
{
    public Task<UserDto> Handle(MediatRQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new UserDto(request.UserId, "John Doe"));
    }
}

public record MediatRPipelineQuery(int UserId) : IRequest<UserDto>;

public class MediatRPipelineQueryHandler : IRequestHandler<MediatRPipelineQuery, UserDto>
{
    public Task<UserDto> Handle(MediatRPipelineQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new UserDto(request.UserId, "John Doe"));
    }
}

// ============================================================================
// Pipeline Behavior for MediatR
// ============================================================================

public class TimingPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Simulate timing overhead (same as TimingPipeline)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();
        return response;
    }
}
