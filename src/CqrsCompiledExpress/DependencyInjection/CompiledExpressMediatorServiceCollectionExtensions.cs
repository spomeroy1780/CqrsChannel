using System.Reflection;
using CqrsCompiledExpress.Mediator;
using CqrsCompiledExpress.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;

namespace CqrsCompiledExpress.DependencyInjection;

public static class CompiledExpressMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Basic registration - just adds the CompiledExpressMediator singleton
    /// </summary>
    public static IServiceCollection AddCompiledExpressMediator(this IServiceCollection services)
    {
        services.AddSingleton<CompiledExpressMediator>();
        return services;
    }

    /// <summary>
    /// All-in-one method: Registers CompiledExpressMediator + auto-discovers and registers all handlers from specified assemblies
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="assemblies">Assemblies to scan for handlers (defaults to calling assembly)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddCompiledExpressMediatorWithHandlers(
        this IServiceCollection services, 
        params Assembly[] assemblies)
    {
        // Default to calling assembly if none specified
        if (assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        // Register the mediator
        services.AddSingleton<CompiledExpressMediator>();

        // Auto-discover and register all handlers
        foreach (var assembly in assemblies)
        {
            RegisterHandlersFromAssembly(services, assembly);
        }

        // Register the startup filter for handler compilation
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, CompiledExpressMediatorStartupFilter>());

        return services;
    }

    /// <summary>
    /// Ultimate convenience method - scans current assembly + referenced assemblies
    /// </summary>
    public static IServiceCollection AddCompiledExpressMediatorWithAutoDiscovery(this IServiceCollection services)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        var assemblies = new List<Assembly> { callingAssembly };
        
        // Add referenced assemblies that might contain handlers
        assemblies.AddRange(callingAssembly.GetReferencedAssemblies()
            .Where(name => !name.Name!.StartsWith("System.") && 
                          !name.Name.StartsWith("Microsoft.") &&
                          !name.Name.StartsWith("netstandard"))
            .Select(Assembly.Load));

        return AddCompiledExpressMediatorWithHandlers(services, assemblies.ToArray());
    }

    private static void RegisterHandlersFromAssembly(IServiceCollection services, Assembly assembly)
    {
        var types = assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract);

        foreach (var type in types)
        {
            // Register Query Handlers
            var queryHandlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
            
            foreach (var @interface in queryHandlerInterfaces)
            {
                services.AddTransient(@interface, type);
            }

            // Register Command Handlers  
            var commandHandlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>));
            
            foreach (var @interface in commandHandlerInterfaces)
            {
                services.AddTransient(@interface, type);
            }

            // Register Event Handlers
            var eventHandlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));
            
            foreach (var @interface in eventHandlerInterfaces)
            {
                services.AddTransient(@interface, type);
            }
        }
    }
}
