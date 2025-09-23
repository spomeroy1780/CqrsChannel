using CqrsCompiledExpress.Contracts;

namespace CqrsCompiledExpress.Contracts;

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    ValueTask<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}