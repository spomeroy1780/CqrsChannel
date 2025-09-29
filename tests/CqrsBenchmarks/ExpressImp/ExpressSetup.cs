using CqrsExpress.Core;
using CqrsExpress.Contracts;
using Microsoft.Extensions.DependencyInjection;
using CqrsBenchmarks.ExpressImp;

namespace CqrsBenchmarks.ExpressImp;

public static class ExpressSetup
{
    public static ExpressMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddTransient<IQueryHandler<GetUserQuery, UserDto>, GetUserHandler>();
        var serviceProvider = services.BuildServiceProvider();
        
        // Pre-compile handlers for maximum performance
        ExpressMediator.PreCompileHandlers(typeof(GetUserHandler).Assembly);
        
        return new ExpressMediator(serviceProvider);
    }
}