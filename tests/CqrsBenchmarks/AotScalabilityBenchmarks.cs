using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using CqrsExpress.Contracts;
using CqrsExpress.Core.Aot;
using CqrsExpress.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsBenchmarks;

/// <summary>
/// Scalability benchmarks testing AOT performance with varying numbers of registered handlers
/// Demonstrates O(1) lookup performance regardless of registry size
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AotScalabilityBenchmarks
{
    private IServiceProvider _serviceProvider10 = null!;
    private IServiceProvider _serviceProvider100 = null!;
    private IServiceProvider _serviceProvider1000 = null!;
    private IServiceProvider _serviceProvider10000 = null!;
    
    private AotExpressMediator _mediator10 = null!;
    private AotExpressMediator _mediator100 = null!;
    private AotExpressMediator _mediator1000 = null!;
    private AotExpressMediator _mediator10000 = null!;
    
    private ScalabilityQuery1 _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        _query = new ScalabilityQuery1(1);
        
        // Setup with 10 handlers
        var services10 = new ServiceCollection();
        services10.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<ScalabilityQuery1, ScalabilityResult>();
            // Register 9 additional dummy handlers (different query types)
            for (int i = 2; i <= 10; i++)
            {
                RegisterDummyQuery(registry, i);
            }
        });
        services10.AddSingleton<IQueryHandler<ScalabilityQuery1, ScalabilityResult>, ScalabilityQueryHandler1>();
        RegisterDummyHandlers(services10, 2, 10);
        _serviceProvider10 = services10.BuildServiceProvider();
        _mediator10 = _serviceProvider10.GetRequiredService<AotExpressMediator>();
        
        // Setup with 100 handlers
        var services100 = new ServiceCollection();
        services100.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<ScalabilityQuery1, ScalabilityResult>();
            for (int i = 2; i <= 100; i++)
            {
                RegisterDummyQuery(registry, i);
            }
        });
        services100.AddSingleton<IQueryHandler<ScalabilityQuery1, ScalabilityResult>, ScalabilityQueryHandler1>();
        RegisterDummyHandlers(services100, 2, 100);
        _serviceProvider100 = services100.BuildServiceProvider();
        _mediator100 = _serviceProvider100.GetRequiredService<AotExpressMediator>();
        
        // Setup with 1000 handlers
        var services1000 = new ServiceCollection();
        services1000.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<ScalabilityQuery1, ScalabilityResult>();
            for (int i = 2; i <= 1000; i++)
            {
                RegisterDummyQuery(registry, i);
            }
        });
        services1000.AddSingleton<IQueryHandler<ScalabilityQuery1, ScalabilityResult>, ScalabilityQueryHandler1>();
        RegisterDummyHandlers(services1000, 2, 1000);
        _serviceProvider1000 = services1000.BuildServiceProvider();
        _mediator1000 = _serviceProvider1000.GetRequiredService<AotExpressMediator>();
        
        // Setup with 10000 handlers
        var services10000 = new ServiceCollection();
        services10000.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<ScalabilityQuery1, ScalabilityResult>();
            for (int i = 2; i <= 10000; i++)
            {
                RegisterDummyQuery(registry, i);
            }
        });
        services10000.AddSingleton<IQueryHandler<ScalabilityQuery1, ScalabilityResult>, ScalabilityQueryHandler1>();
        RegisterDummyHandlers(services10000, 2, 10000);
        _serviceProvider10000 = services10000.BuildServiceProvider();
        _mediator10000 = _serviceProvider10000.GetRequiredService<AotExpressMediator>();
    }

    private void RegisterDummyQuery(IAotHandlerRegistry registry, int index)
    {
        // Register a generic dummy handler - we won't actually call these
        // This demonstrates registry size impact on lookup performance
        var dummyInvoker = (IQueryHandler<DummyQuery, ScalabilityResult> handler, DummyQuery query, CancellationToken ct) =>
        {
            return new ValueTask<ScalabilityResult>(new ScalabilityResult { Value = query.Id, ProcessedAt = DateTime.UtcNow });
        };
        registry.RegisterQueryHandler(dummyInvoker);
    }

    private void RegisterDummyHandlers(IServiceCollection services, int start, int count)
    {
        // Register dummy handler instances (they all use the same implementation)
        services.AddSingleton<IQueryHandler<DummyQuery, ScalabilityResult>, DummyQueryHandler>();
    }

    [Benchmark(Baseline = true)]
    public async Task<ScalabilityResult> AotMediator_10Handlers()
    {
        return await _mediator10.Send<ScalabilityQuery1, ScalabilityResult>(_query);
    }
    
    [Benchmark]
    public async Task<ScalabilityResult> AotMediator_100Handlers()
    {
        return await _mediator100.Send<ScalabilityQuery1, ScalabilityResult>(_query);
    }
    
    [Benchmark]
    public async Task<ScalabilityResult> AotMediator_1000Handlers()
    {
        return await _mediator1000.Send<ScalabilityQuery1, ScalabilityResult>(_query);
    }
    
    [Benchmark]
    public async Task<ScalabilityResult> AotMediator_10000Handlers()
    {
        return await _mediator10000.Send<ScalabilityQuery1, ScalabilityResult>(_query);
    }
}

// Query and handler definitions for scalability benchmarks
public record ScalabilityQuery1(int Id) : IQuery<ScalabilityResult>;

public record ScalabilityResult
{
    public int Value { get; init; }
    public DateTime ProcessedAt { get; init; }
}

internal class ScalabilityQueryHandler1 : IQueryHandler<ScalabilityQuery1, ScalabilityResult>
{
    public ValueTask<ScalabilityResult> Handle(ScalabilityQuery1 query, CancellationToken cancellationToken)
    {
        return new ValueTask<ScalabilityResult>(new ScalabilityResult 
        { 
            Value = query.Id,
            ProcessedAt = DateTime.UtcNow
        });
    }
}

// Dummy query/handler for registry population
public record DummyQuery(int Id) : IQuery<ScalabilityResult>;

internal class DummyQueryHandler : IQueryHandler<DummyQuery, ScalabilityResult>
{
    public ValueTask<ScalabilityResult> Handle(DummyQuery query, CancellationToken cancellationToken)
    {
        return new ValueTask<ScalabilityResult>(new ScalabilityResult 
        { 
            Value = query.Id,
            ProcessedAt = DateTime.UtcNow
        });
    }
}
