using CqrsExpress.Contracts;

namespace CqrsBenchmarks.ExpressImp;

/// <summary>
/// ULTRA high-performance struct-based handler.
/// Eliminates virtual call overhead by using a struct instead of interface.
/// This should beat MessagePipe!
/// </summary>
public struct GetUserHandlerStruct : IQueryHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new UserDto(request.Id, "Alice"));
}
