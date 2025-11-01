namespace CqrsExpress.Contracts;


public interface IQueryHandlerFast<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : notnull
{
    /// <summary>
    /// Handles the query and returns a non-nullable result.
    /// </summary>
    ValueTask<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}
