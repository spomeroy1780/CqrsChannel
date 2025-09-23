using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsBenchmarks.MessagePipeImpl;

public static class MessagePipeSetup
{
    public static IRequestHandler<MessagePipeQuery, UserDto> BuildHandler()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        services.AddTransient<IRequestHandler<MessagePipeQuery, UserDto>, MessagePipeHandler>();
        return services.BuildServiceProvider()
                       .GetRequiredService<IRequestHandler<MessagePipeQuery, UserDto>>();
    }
}
