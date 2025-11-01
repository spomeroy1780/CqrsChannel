using System.Reflection;
using CqrsExpress.Core;
using CqrsExpress.Contracts;
using CqrsExpress.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace CqrsExpress.DependencyInjection;

public static class ExpressMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Basic registration - just adds the ExpressMediator singleton
    /// </summary>
    public static IServiceCollection AddExpressMediator(this IServiceCollection services)
    {
        services.AddSingleton<ExpressMediator>();
        return services;
    }

    /// <summary>
    /// Register ExpressMediator with pipeline support
    /// Zero-allocation pipeline execution for maximum performance
    /// </summary>
    public static IServiceCollection AddExpressMediatorWithPipelines(
        this IServiceCollection services,
        Action<ExpressMediatorOptions>? configure = null)
    {
        var options = new ExpressMediatorOptions();
        configure?.Invoke(options);

        // Register pipelines as singletons for reuse
        foreach (var pipelineType in options.GlobalPipelines)
        {
            services.TryAddSingleton(pipelineType);
        }

        foreach (var pipelineTypes in options.PerTypePipelines.Values)
        {
            foreach (var pipelineType in pipelineTypes)
            {
                services.TryAddSingleton(pipelineType);
            }
        }

        // Register ExpressMediator with pipeline resolution
        services.AddSingleton<ExpressMediator>(provider =>
        {
            var globalPipelines = options.GlobalPipelines
                .Select(t => (IRequestPipeline)provider.GetRequiredService(t))
                .ToList();

            var perTypePipelines = new Dictionary<Type, List<IRequestPipeline>>();
            foreach (var kvp in options.PerTypePipelines)
            {
                perTypePipelines[kvp.Key] = kvp.Value
                    .Select(t => (IRequestPipeline)provider.GetRequiredService(t))
                    .ToList();
            }

            return new ExpressMediator(
                provider,
                globalPipelines.Count > 0 ? globalPipelines : null,
                perTypePipelines.Count > 0 ? perTypePipelines : null);
        });

        return services;
    }

    /// <summary>
    /// High-performance registration with pre-compilation of handlers at startup
    /// Eliminates first-request overhead by pre-compiling expression trees
    /// </summary>
    public static IServiceCollection AddExpressMediatorWithPreCompilation(
        this IServiceCollection services, 
        params Assembly[] assemblies)
    {
        // Default to calling assembly if none specified
        if (assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        // Register the mediator
        services.AddSingleton<ExpressMediator>();

        // Auto-discover and register all handlers
        foreach (var assembly in assemblies)
        {
            RegisterHandlersFromAssembly(services, assembly);
        }

        // Register hosted service for pre-compilation at startup
        services.AddHostedService<ExpressMediatorPreCompilationService>(provider => 
            new ExpressMediatorPreCompilationService(assemblies));

        return services;
    }

    /// <summary>
    /// All-in-one method: Registers ExpressMediator + auto-discovers and registers all handlers from specified assemblies
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="assemblies">Assemblies to scan for handlers (defaults to calling assembly)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddExpressMediatorWithHandlers(
        this IServiceCollection services, 
        params Assembly[] assemblies)
    {
        // Default to calling assembly if none specified
        if (assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        // Register the mediator
        services.AddSingleton<ExpressMediator>();

        // Auto-discover and register all handlers
        foreach (var assembly in assemblies)
        {
            RegisterHandlersFromAssembly(services, assembly);
        }

        // Register the startup filter for handler compilation
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, ExpressMediatorStartupFilter>());

        return services;
    }

    /// <summary>
    /// Ultimate convenience method - scans current assembly + referenced assemblies
    /// </summary>
    public static IServiceCollection AddExpressMediatorWithAutoDiscovery(this IServiceCollection services)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        var assemblies = new List<Assembly> { callingAssembly };
        
        // Add referenced assemblies that might contain handlers
        assemblies.AddRange(callingAssembly.GetReferencedAssemblies()
            .Where(name => !name.Name!.StartsWith("System.") && 
                          !name.Name.StartsWith("Microsoft.") &&
                          !name.Name.StartsWith("netstandard"))
            .Select(Assembly.Load));

        return AddExpressMediatorWithHandlers(services, assemblies.ToArray());
    }

    /// <summary>
    /// scans current assembly + referenced assemblies and registers handlers and registers pre-compilation service
    /// </summary>
    /// <param name="services"></param>
    /// <param name="assembly"></param>
    public static IServiceCollection AddExpressMediatorWithAutoDiscoveryAndPreCompilation(this IServiceCollection services)
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        var assemblies = new List<Assembly> { callingAssembly };

        // Add referenced assemblies that might contain handlers
        assemblies.AddRange(callingAssembly.GetReferencedAssemblies()
            .Where(name => !name.Name!.StartsWith("System.") &&
                          !name.Name.StartsWith("Microsoft.") &&
                          !name.Name.StartsWith("netstandard"))
            .Select(Assembly.Load));

        return AddExpressMediatorWithPreCompilation(services, [.. assemblies]);
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
