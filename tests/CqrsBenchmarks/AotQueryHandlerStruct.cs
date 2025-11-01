using CqrsExpress.Contracts;

namespace CqrsBenchmarks;

/// <summary>
/// Struct-based AOT query handler for ultra-fast performance.
/// Eliminates virtual call overhead.
/// </summary>
public struct AotQueryHandlerStruct : IQueryHandlerFast<AotQuery, UserDto>
{
    public ValueTask<UserDto> Handle(AotQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new UserDto(query.UserId, "John Doe"));
}
