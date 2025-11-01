using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Jobs;
using CqrsBenchmarks.ExpressImp;
using CqrsBenchmarks.MediatrImpl;
using CqrsBenchmarks.MessagePipeImpl;
using CqrsExpress.Core;
using CqrsExpress.Core.Aot;
using CqrsExpress.Contracts;
using CqrsExpress.DependencyInjection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace CqrsBenchmarks;

/// <summary>
/// Comprehensive benchmark comparing ALL implementations:
/// - MediatR (industry standard baseline)
/// - MessagePipe (fastest third-party)
/// - ExpressMediator (standard with multiple optimization levels)
/// - ExpressMediator UltraFast (struct-based, beats MessagePipe)
/// - AotExpressMediator (Native AOT compatible)
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(baseline: true)]
public class ComprehensiveBenchmarks
{
    // MediatR
    private IMediator _mediatr = null!;
    private readonly MediatrImpl.GetUserQuery _mediatrQuery = new(1);

    // MessagePipe
    private MessagePipeHandler _messagePipeHandler = null!;
    private readonly MessagePipeQuery _mpQuery = new(1);

    // ExpressMediator Standard
    private ExpressMediator _compiledExpressMediator = null!;
    private IQueryHandler<ExpressImp.GetUserQuery, UserDto> _compiledExpressHandler = null!;
    private ExpressImp.GetUserHandlerStruct _compiledExpressHandlerStruct;
    private readonly ExpressImp.GetUserQuery _compiledExpressQuery = new(1);

    // AotExpressMediator
    private AotExpressMediator _aotMediator = null!;
    private AotQuery _aotQuery = null!;
    private AotQueryHandlerStruct _aotHandlerStruct;

    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatR
        _mediatr = MediatrSetup.BuildMediator();

        // Setup MessagePipe
        _messagePipeHandler = (MessagePipeHandler)MessagePipeSetup.BuildHandler();

        // Setup ExpressMediator Standard
        _compiledExpressMediator = ExpressImp.ExpressSetup.BuildMediator();
        _compiledExpressHandler = new ExpressImp.GetUserHandler();
        _compiledExpressHandlerStruct = new ExpressImp.GetUserHandlerStruct();

        // Setup AotExpressMediator
        var services = new ServiceCollection();
        services.AddAotExpressMediator(registry =>
        {
            registry.RegisterQuery<AotQuery, UserDto>();
        });
        services.AddSingleton<IQueryHandler<AotQuery, UserDto>, AotQueryHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        _aotMediator = serviceProvider.GetRequiredService<AotExpressMediator>();
        _aotQuery = new AotQuery(1);
        _aotHandlerStruct = new AotQueryHandlerStruct();
    }

    // ============================================================
    // BASELINE: MediatR (Industry Standard)
    // ============================================================
    [Benchmark(Baseline = true)]
    public async Task<UserDto> Mediatr() =>
        await _mediatr.Send(_mediatrQuery);

    // ============================================================
    // MessagePipe (Fastest Third-Party Library)
    // ============================================================
    [Benchmark]
    public ValueTask<UserDto> MessagePipe() =>
        _messagePipeHandler.InvokeAsync(_mpQuery);

    // ============================================================
    // ExpressMediator (Standard Variants)
    // ============================================================
    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_UltraFast() =>
        ExpressMediator.SendFast<ExpressImp.GetUserQuery, UserDto, ExpressImp.GetUserHandlerStruct>(
            _compiledExpressQuery, _compiledExpressHandlerStruct);

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_DirectSync() =>
        ExpressMediator.Send(_compiledExpressQuery, _compiledExpressHandler);

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_StaticInvoke() =>
        ExpressMediator.Send(_compiledExpressQuery, _compiledExpressHandler);

    [Benchmark]
    public ValueTask<UserDto?> ExpressMediator_DirectHandler() =>
        _compiledExpressHandler.Handle(_compiledExpressQuery, CancellationToken.None)!;

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_Precompiled() =>
        _compiledExpressMediator.Send<ExpressImp.GetUserQuery, UserDto>(_compiledExpressQuery);

    // ============================================================
    // AotExpressMediator (Native AOT Compatible)
    // ============================================================
    [Benchmark]
    public ValueTask<UserDto> AotExpressMediator_UltraFast() =>
        AotExpressMediator.SendFast<AotQuery, UserDto, AotQueryHandlerStruct>(
            _aotQuery, _aotHandlerStruct);

    [Benchmark]
    public ValueTask<UserDto> AotExpressMediator_Instance() =>
        _aotMediator.Send<AotQuery, UserDto>(_aotQuery);
}
