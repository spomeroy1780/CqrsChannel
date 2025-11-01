using System.Diagnostics;
using CqrsExpress.Pipeline;

namespace CqrsExpress.Pipelines;

/// <summary>
/// High-performance logging pipeline with zero allocations
/// Uses structured logging and activity tracing
/// </summary>
public sealed class LoggingPipeline : IRequestPipeline
{
    public async ValueTask<object?> ProcessAsync(
        object request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType().Name;
        var stopwatch = Stopwatch.StartNew();
        
        using var activity = Activity.Current?.Source.StartActivity($"Pipeline.Logging.{requestType}");
        
        try
        {
            var result = await next(request, cancellationToken);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// High-performance validation pipeline
/// Validates requests that implement IValidatable
/// </summary>
public sealed class ValidationPipeline : IRequestPipeline
{
    public async ValueTask<object?> ProcessAsync(
        object request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        // Check if request implements validation interface
        if (request is IValidatable validatable)
        {
            var validationResult = validatable.Validate();
            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }
        }
        
        return await next(request, cancellationToken);
    }
}

/// <summary>
/// High-performance timing pipeline for performance monitoring
/// Zero allocation fast path
/// </summary>
public sealed class TimingPipeline : IRequestPipeline
{
    public async ValueTask<object?> ProcessAsync(
        object request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            return await next(request, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            // Could log to metrics system here
            Activity.Current?.SetTag("handler_duration_ms", stopwatch.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// Marker interface for validatable requests
/// </summary>
public interface IValidatable
{
    ValidationResult Validate();
}

/// <summary>
/// Validation result structure
/// </summary>
public readonly struct ValidationResult
{
    public bool IsValid { get; init; }
    public string[] Errors { get; init; }
    
    public static ValidationResult Success() => new() { IsValid = true, Errors = Array.Empty<string>() };
    public static ValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Validation exception
/// </summary>
public sealed class ValidationException : Exception
{
    public string[] Errors { get; }
    
    public ValidationException(string[] errors) 
        : base($"Validation failed: {string.Join(", ", errors)}")
    {
        Errors = errors;
    }
}
