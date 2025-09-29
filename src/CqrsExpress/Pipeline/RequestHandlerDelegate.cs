namespace CqrsExpress.Pipeline;

public delegate ValueTask<object?> RequestHandlerDelegate(object request, CancellationToken cancellationToken);
