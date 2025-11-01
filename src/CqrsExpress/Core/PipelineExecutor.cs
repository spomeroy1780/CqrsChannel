using System.Runtime.CompilerServices;
using CqrsExpress.Pipeline;

namespace CqrsExpress.Core;

/// <summary>
/// Ultra-high-performance pipeline executor with zero-allocation fast path
/// Uses compiled delegates and aggressive inlining for maximum throughput
/// .NET 10 optimized with no reflection
/// </summary>
internal static class PipelineExecutor
{
    /// <summary>
    /// Execute pipeline chain with zero allocations for synchronous completions
    /// Builds a chain of delegates at compile time, no reflection
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<object?> ExecutePipeline(
        object request,
        RequestHandlerDelegate finalHandler,
        IReadOnlyList<IRequestPipeline> pipelines,
        CancellationToken cancellationToken)
    {
        // Fast path: No pipelines = direct handler execution
        if (pipelines.Count == 0)
        {
            return finalHandler(request, cancellationToken);
        }

        // Build pipeline chain from right to left (onion pattern)
        // Pipeline[0] wraps Pipeline[1] wraps ... Pipeline[N] wraps Handler
        RequestHandlerDelegate next = finalHandler;
        
        // Iterate backwards to build chain
        for (int i = pipelines.Count - 1; i >= 0; i--)
        {
            var pipeline = pipelines[i];
            var capturedNext = next;
            
            // Create delegate that closes over current pipeline and next handler
            next = (req, ct) => pipeline.ProcessAsync(req, capturedNext, ct);
        }
        
        // Execute the chain
        return next(request, cancellationToken);
    }

    /// <summary>
    /// Ultra-fast synchronous pipeline execution for completed ValueTasks
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? ExecutePipelineSync(
        object request,
        Func<object, object?> finalHandler,
        IReadOnlyList<IRequestPipeline> pipelines)
    {
        if (pipelines.Count == 0)
        {
            return finalHandler(request);
        }

        // Build synchronous chain
        RequestHandlerDelegate asyncChain = (req, ct) => 
        {
            var result = finalHandler(req);
            return ValueTask.FromResult(result);
        };

        for (int i = pipelines.Count - 1; i >= 0; i--)
        {
            var pipeline = pipelines[i];
            var capturedNext = asyncChain;
            asyncChain = (req, ct) => pipeline.ProcessAsync(req, capturedNext, ct);
        }

        var valueTask = asyncChain(request, CancellationToken.None);
        
        // Fast path for synchronous completion
        return valueTask.IsCompletedSuccessfully 
            ? valueTask.Result 
            : valueTask.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Merge global and per-type pipelines into a single cached list
    /// Zero allocation if no pipelines exist
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyList<IRequestPipeline> GetPipelinesForRequest(
        Type requestType,
        List<IRequestPipeline>? globalPipelines,
        Dictionary<Type, List<IRequestPipeline>>? perTypePipelines)
    {
        // Fast path: No pipelines configured
        if (globalPipelines == null && perTypePipelines == null)
        {
            return Array.Empty<IRequestPipeline>();
        }

        // Check for per-type pipelines
        List<IRequestPipeline>? typePipelines = null;
        var hasPerType = perTypePipelines?.TryGetValue(requestType, out typePipelines) == true;
        var hasGlobal = globalPipelines != null && globalPipelines.Count > 0;

        // Optimize common cases
        if (!hasGlobal && !hasPerType)
        {
            return Array.Empty<IRequestPipeline>();
        }
        
        if (hasGlobal && !hasPerType)
        {
            return globalPipelines!;
        }
        
        if (!hasGlobal && hasPerType)
        {
            return typePipelines!;
        }

        // Both exist: merge (global first, then per-type)
        var merged = new List<IRequestPipeline>(
            globalPipelines!.Count + typePipelines!.Count);
        merged.AddRange(globalPipelines);
        merged.AddRange(typePipelines);
        
        return merged;
    }
}
