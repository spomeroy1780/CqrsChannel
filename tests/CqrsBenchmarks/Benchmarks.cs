using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Jobs;
using CqrsBenchmarks.ExpressImp;
using CqrsBenchmarks.MediatrImpl;
using CqrsBenchmarks.MessagePipeImpl;
using CqrsExpress.Core;
using CqrsExpress.Contracts;
using MediatR;

namespace CqrsBenchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(baseline: true)]
public class Benchmarks
{
    private IMediator _mediatr = null!;
    private MessagePipe.IRequestHandler<MessagePipeQuery, UserDto> _messagePipeHandler = null!;
    private ExpressMediator _compiledExpressMediator = null!;
    private IQueryHandler<ExpressImp.GetUserQuery, UserDto> _compiledExpressHandler = null!;

    private readonly MediatrImpl.GetUserQuery _mediatrQuery = new(1);
    private readonly MessagePipeQuery _mpQuery = new(1);
    private readonly ExpressImp.GetUserQuery _compiledExpressQuery = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mediatr = MediatrSetup.BuildMediator();
        _messagePipeHandler = MessagePipeSetup.BuildHandler();
        _compiledExpressMediator = ExpressImp.ExpressSetup.BuildMediator();
        _compiledExpressHandler = new ExpressImp.GetUserHandler();
    }

    [Benchmark(Baseline = true)]
    public async Task<UserDto> Mediatr() =>
        await _mediatr.Send(_mediatrQuery);

    [Benchmark]
    public async Task<UserDto> MessagePipe() =>
        await ((MessagePipeHandler)_messagePipeHandler).InvokeAsync(_mpQuery);

    [Benchmark]
    public async ValueTask<UserDto> CqrsExpress() =>
        await _compiledExpressMediator.Send<ExpressImp.GetUserQuery, UserDto>(_compiledExpressQuery, _compiledExpressHandler);

    [Benchmark]
    public async ValueTask<UserDto> CqrsExpressOptimized() =>
        await _compiledExpressMediator.Send<ExpressImp.GetUserQuery, UserDto>(_compiledExpressQuery);

    [Benchmark]
    public async ValueTask<UserDto> CqrsExpressDirect() =>
        await _compiledExpressHandler.Handle(_compiledExpressQuery, CancellationToken.None);

    [Benchmark]
    public UserDto CqrsExpressDirectSync() =>
        ExpressMediator.SendSync(_compiledExpressQuery, _compiledExpressHandler);
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
    public async Task<UserDto[]> ExpressLoad()
    {
        var tasks = new ValueTask<UserDto>[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            tasks[i] = _compiledExpressMediator.Send<ExpressImp.GetUserQuery, UserDto>(_compiledExpressQuery, _compiledExpressHandler);

        var results = new UserDto[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            results[i] = await tasks[i];

        return results;
    }

    [Benchmark]
    public async Task<UserDto[]> ExpressDirectLoad()
    {
        var tasks = new ValueTask<UserDto>[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            tasks[i] = _compiledExpressHandler.Handle(_compiledExpressQuery, CancellationToken.None);

        var results = new UserDto[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            results[i] = await tasks[i];
            
        return results;
    }

    [Benchmark]
    public UserDto[] ExpressDirectSyncLoad()
    {
        var results = new UserDto[LoadSize];
        for (int i = 0; i < LoadSize; i++)
            results[i] = ExpressMediator.SendSync(_compiledExpressQuery, _compiledExpressHandler);

        return results;
    }
}

public record UserDto(int Id, string Name);
