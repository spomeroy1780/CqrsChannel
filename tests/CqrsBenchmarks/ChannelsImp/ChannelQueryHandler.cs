using CqrsCompiledExpress.Contracts;

namespace CqrsBenchmarks.ChannelsImp;

public class ChannelQueryHandler : IQueryHandler<ChannelQuery, UserDto>
{
    public ValueTask<UserDto> Handle(ChannelQuery request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new UserDto(request.Id, "Alice"));
}