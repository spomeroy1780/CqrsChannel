using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Jobs;
using CqrsBenchmarks.ExpressImp;
using CqrsBenchmarks.MediatrImpl;
using CqrsBenchmarks.MessagePipeImpl;
using CqrsExpress.Core;
using CqrsExpress.Contracts;
using MediatR;
using System.Threading.Tasks;

namespace CqrsBenchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(baseline: true)]
public class Benchmarks
{
    private IMediator _mediatr = null!;
    private MessagePipeHandler _messagePipeHandler = null!;
    private ExpressMediator _compiledExpressMediator = null!;
    private IQueryHandler<ExpressImp.GetUserQuery, UserDto> _compiledExpressHandler = null!;
    private IQueryHandler<ExpressImp.GetUserQuery, UserDto> _compiledExpressHandlerFast = null!;
    private ExpressImp.GetUserHandlerStruct _compiledExpressHandlerStruct;

    private readonly MediatrImpl.GetUserQuery _mediatrQuery = new(1);
    private readonly MessagePipeQuery _mpQuery = new(1);
    private readonly ExpressImp.GetUserQuery _compiledExpressQuery = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mediatr = MediatrSetup.BuildMediator();
        _messagePipeHandler = (MessagePipeHandler)MessagePipeSetup.BuildHandler();
        _compiledExpressMediator = ExpressImp.ExpressSetup.BuildMediator();
        _compiledExpressHandler = new ExpressImp.GetUserHandler();
        _compiledExpressHandlerFast = new ExpressImp.GetUserHandlerFast();
        _compiledExpressHandlerStruct = new ExpressImp.GetUserHandlerStruct();
    }

    [Benchmark(Baseline = true)]
    public async Task<UserDto> Mediatr() =>
        await _mediatr.Send(_mediatrQuery);

    [Benchmark]
    public ValueTask<UserDto> MessagePipe() =>
        _messagePipeHandler.InvokeAsync(_mpQuery);

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_UltraFast() =>
        ExpressMediator.Send(_compiledExpressQuery, _compiledExpressHandlerStruct);

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_StaticInvoke() =>
        ExpressMediator.Send(_compiledExpressQuery, _compiledExpressHandler);

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_Precompiled() =>
        _compiledExpressMediator.Send<ExpressImp.GetUserQuery, UserDto>(_compiledExpressQuery);

    [Benchmark]
    public ValueTask<UserDto?> ExpressMediator_DirectHandler() =>
        _compiledExpressHandler.Handle(_compiledExpressQuery, CancellationToken.None)!;

    [Benchmark]
    public ValueTask<UserDto> ExpressMediator_DirectSync() =>
        ExpressMediator.Send(_compiledExpressQuery, _compiledExpressHandler);
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(baseline: true)]
public class LoadBenchmarks
{
    private IMediator _mediatr = null!;
    private MessagePipe.IRequestHandler<MessagePipeQuery, UserDto> _messagePipeHandler = null!;
    private ExpressMediator _compiledExpressMediator = null!;
    private IQueryHandler<ExpressImp.GetUserQuery, UserDto> _compiledExpressHandler = null!;

    private readonly MediatrImpl.GetUserQuery _mediatrQuery = new(1);
    private readonly MessagePipeQuery _mpQuery = new(1);
    private readonly ExpressImp.GetUserQuery _compiledExpressQuery = new(1);

    [Params(500, 1000, 2000, 5000)]
    public int LoadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _mediatr = MediatrSetup.BuildMediator();
        _messagePipeHandler = MessagePipeSetup.BuildHandler();
        _compiledExpressMediator = ExpressImp.ExpressSetup.BuildMediator();
        _compiledExpressHandler = new ExpressImp.GetUserHandler();
    }

    [Benchmark(Baseline = true)]
    public async Task<UserDto[]> MediatrLoad()
    {
        var tasks = new Task<UserDto>[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            tasks[i] = _mediatr.Send(_mediatrQuery);
        
        return await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task<UserDto[]> MessagePipeLoad()
    {
        var tasks = new Task<UserDto>[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            tasks[i] = ((MessagePipeHandler)_messagePipeHandler).InvokeAsync(_mpQuery).AsTask();
        
        return await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task<UserDto[]> ExpressMediator_StaticInvoke_Load()
    {
        var tasks = new ValueTask<UserDto>[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            tasks[i] = ExpressMediator.Send(_compiledExpressQuery, _compiledExpressHandler);

        var results = new UserDto[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            results[i] = await tasks[i];

        return results;
    }

    [Benchmark]
    public async Task<UserDto[]> ExpressMediator_Precompiled_Load()
    {
        var tasks = new ValueTask<UserDto>[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            tasks[i] = _compiledExpressMediator.Send<ExpressImp.GetUserQuery, UserDto>(_compiledExpressQuery);

        var results = new UserDto[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            results[i] = await tasks[i];
            
        return results;
    }

    [Benchmark]
    public async Task<UserDto[]> ExpressMediator_DirectHandler_Load()
    {
        UserDto[] results = new UserDto[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            results[i] = await ExpressMediator.SendDirect(_compiledExpressQuery, _compiledExpressHandler);

        return results;
    }
}

public record UserDto(int Id, string Name);
