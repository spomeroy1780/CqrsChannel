using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CqrsExpress.Core;

namespace CqrsExpress.DependencyInjection;

/// <summary>
/// Background service that pre-compiles expression trees for all handlers at startup
/// This eliminates the initial overhead on first request
/// </summary>
public class ExpressMediatorPreCompilationService : IHostedService
{
    private readonly Assembly[] _assemblies;
    private readonly ILogger<ExpressMediatorPreCompilationService>? _logger;

    public ExpressMediatorPreCompilationService(Assembly[] assemblies, ILogger<ExpressMediatorPreCompilationService>? logger = null)
    {
        _assemblies = assemblies;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting ExpressMediator pre-compilation for {AssemblyCount} assemblies", _assemblies.Length);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            ExpressMediator.PreCompileHandlers(_assemblies);
            
            stopwatch.Stop();
            _logger?.LogInformation("ExpressMediator pre-compilation completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to pre-compile ExpressMediator handlers");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}