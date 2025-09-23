using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CqrsCompiledExpress.DependencyInjection;

internal sealed class CompiledExpressMediatorStartupFilter : IStartupFilter
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
