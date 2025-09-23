using CqrsCompiledExpress.Mediator;
using CqrsCompiledExpress.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsBenchmarks.ChannelsImp;

public static class ChannelSetup
{
    public static CompiledExpressMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddTransient<IQueryHandler<ChannelQuery, UserDto>, ChannelQueryHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        return new CompiledExpressMediator(serviceProvider);
    }
}