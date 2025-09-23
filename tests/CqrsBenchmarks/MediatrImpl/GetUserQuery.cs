using MediatR;

namespace CqrsBenchmarks.MediatrImpl;

public record GetUserQuery(int Id) : IRequest<UserDto>;
