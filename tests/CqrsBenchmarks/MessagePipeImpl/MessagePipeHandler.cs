using MessagePipe;
namespace CqrsBenchmarks.MessagePipeImpl;

public class MessagePipeHandler : IRequestHandler<MessagePipeQuery, UserDto>
{
    public ValueTask<UserDto> InvokeAsync(MessagePipeQuery request, CancellationToken ct = default) =>
        ValueTask.FromResult(new UserDto(request.Id, "Alice"));

    public UserDto Invoke(MessagePipeQuery request) =>
        new(request.Id, "Alice");
}

