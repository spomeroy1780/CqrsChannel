namespace CqrsExpress.Pipeline;

public interface IRequestPipeline
{
    ValueTask<object?> ProcessAsync(object request, RequestHandlerDelegate handler, CancellationToken cancellationToken = default);
}
