using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MediatR;
using CqrsExpress.Core;
using CqrsExpress.Contracts;
using CqrsExpress.Pipeline;
using CqrsExpress.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsBenchmarks;

/// <summary>
/// Comprehensive benchmarks comparing MediatR vs ExpressMediator with pipelines
/// Focus: Zero-allocation, high-throughput scenarios
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(baseline: true)]
public class PipelineBenchmarks
{
    private IMediator _mediatr = null!;
    private IMediator _mediatrWithPipeline = null!;
    private ExpressMediator _express = null!;
    private ExpressMediator _expressWithPipeline = null!;
    private IQueryHandler<ExpressPipelineQuery, UserDto> _expressDirectHandler = null!;

    private readonly MediatrQuery _mediatrQuery = new(1);
    private readonly ExpressPipelineQuery _expressQuery = new(1);

    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatR without pipeline
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(typeof(MediatrQuery).Assembly);
        mediatrServices.AddTransient<IRequestHandler<MediatrQuery, UserDto>, MediatrQueryHandler>();
        _mediatr = mediatrServices.BuildServiceProvider().GetRequiredService<IMediator>();

        // Setup MediatR with pipeline
        var mediatrPipelineServices = new ServiceCollection();
        mediatrPipelineServices.AddMediatR(typeof(MediatrQuery).Assembly);
        mediatrPipelineServices.AddTransient<IRequestHandler<MediatrQuery, UserDto>, MediatrQueryHandler>();
        mediatrPipelineServices.AddTransient(typeof(IPipelineBehavior<,>), typeof(MediatrLoggingPipeline<,>));
        _mediatrWithPipeline = mediatrPipelineServices.BuildServiceProvider().GetRequiredService<IMediator>();

        // Setup ExpressMediator without pipeline
        var expressServices = new ServiceCollection();
        expressServices.AddSingleton<ExpressMediator>();
        expressServices.AddTransient<IQueryHandler<ExpressPipelineQuery, UserDto>, ExpressPipelineQueryHandler>();
        var expressProvider = expressServices.BuildServiceProvider();
        _express = expressProvider.GetRequiredService<ExpressMediator>();

        // Setup ExpressMediator with pipeline
        var expressPipelineServices = new ServiceCollection();
        expressPipelineServices.AddSingleton<TimingPipeline>();
        var pipelineProvider = expressPipelineServices.BuildServiceProvider();
        var globalPipelines = new List<IRequestPipeline> { pipelineProvider.GetRequiredService<TimingPipeline>() };
        
        var expressWithPipeServices = new ServiceCollection();
        expressWithPipeServices.AddSingleton(_ => new ExpressMediator(expressProvider, globalPipelines, null));
        expressWithPipeServices.AddTransient<IQueryHandler<ExpressPipelineQuery, UserDto>, ExpressPipelineQueryHandler>();
        _expressWithPipeline = new ExpressMediator(expressProvider, globalPipelines, null);

        // Setup Direct handler
        _expressDirectHandler = new ExpressPipelineQueryHandler();

        // Warmup
        _mediatr.Send(_mediatrQuery).GetAwaiter().GetResult();
        _express.Send(_expressQuery).GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public async Task<UserDto> MediatR_NoPipeline()
    {
        return await _mediatr.Send(_mediatrQuery);
    }

    [Benchmark]
    public async Task<UserDto> MediatR_WithPipeline()
    {
        return await _mediatrWithPipeline.Send(_mediatrQuery);
    }

    [Benchmark]
    public async ValueTask<UserDto> ExpressMediator_NoPipeline()
    {
        return await _express.Send(_expressQuery);
    }

    [Benchmark]
    public async ValueTask<UserDto> ExpressMediator_WithPipeline()
    {
        return await _expressWithPipeline.Send(_expressQuery);
    }

    [Benchmark]
    public async ValueTask<UserDto> ExpressMediator_Direct_NoPipeline()
    {
        return await ExpressMediator.Send(_expressQuery, _expressDirectHandler);
    }
}

// MediatR test types
public record MediatrQuery(int Id) : IRequest<UserDto>;

public class MediatrQueryHandler : IRequestHandler<MediatrQuery, UserDto>
{
    public Task<UserDto> Handle(MediatrQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new UserDto(request.Id, "Alice"));
    }
}

public class MediatrLoggingPipeline<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return await next();
    }
}

// ExpressMediator test types  
public record ExpressPipelineQuery(int Id) : IQuery<UserDto>;

public class ExpressPipelineQueryHandler : IQueryHandler<ExpressPipelineQuery, UserDto>
{
    public ValueTask<UserDto?> Handle(ExpressPipelineQuery request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<UserDto?>(new UserDto(request.Id, "Alice"));
    }
}
