using MediatR;

namespace CqrsBenchmarks.MediatrImpl;

public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery request, CancellationToken ct) =>
        Task.FromResult(new UserDto(request.Id, "Alice"));
}
