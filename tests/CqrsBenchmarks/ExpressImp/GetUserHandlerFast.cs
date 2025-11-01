using CqrsExpress.Contracts;

namespace CqrsBenchmarks.ExpressImp;

/// <summary>
/// High-performance handler using IQueryHandlerFast interface.
/// No nullable overhead - returns ValueTask&lt;UserDto&gt; directly.
/// </summary>
public class GetUserHandlerFast : IQueryHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new UserDto(request.Id, "Alice"));
}
