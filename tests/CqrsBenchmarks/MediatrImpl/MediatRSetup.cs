using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsBenchmarks.MediatrImpl;


public static class MediatrSetup
{
    public static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddMediatR(typeof(GetUserHandler).Assembly);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }
}
