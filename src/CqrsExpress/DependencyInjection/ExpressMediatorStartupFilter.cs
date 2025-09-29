using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace CqrsExpress.DependencyInjection;

internal sealed class ExpressMediatorStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // discover handlers and register invokers
            // keep your reflection logic here
            next(app);
        };
    }
}
