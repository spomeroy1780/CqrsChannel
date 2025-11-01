using System;
using CqrsExpress.Contracts;

namespace CqrsBenchmarks.ExpressImp;

public class GetUserHandler : IQueryHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<UserDto>(new UserDto(request.Id, "Alice"));
}
